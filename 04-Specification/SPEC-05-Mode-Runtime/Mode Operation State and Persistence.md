# SPEC-05 — Mode Operation State and Persistence

## 1. Purpose

Defines the SPEC-05 normative extension to the SPEC-03 machine-state schema.

The base tables from `SPEC-03/SQLite Schema Baseline.md` remain valid except where explicitly extended here.

---

## 2. Physical schema extension goals

Persistence must answer after process crash/reboot:

```text
what mode was canonical?
which mode operation was active?
was target commit durable?
which policy version was being applied?
which actions were already applied?
which blockers/decisions existed?
who owned the machine mutation lease?
```

Persistence still does not decide semantic truth by itself.

---

## 3. `operational_mode_state` extension

The singleton row is extended conceptually with:

```sql
ALTER TABLE operational_mode_state
    ADD COLUMN control_session_key TEXT NULL;

ALTER TABLE operational_mode_state
    ADD COLUMN activation_epoch_id TEXT NULL;

ALTER TABLE operational_mode_state
    ADD COLUMN policy_id TEXT NULL;

ALTER TABLE operational_mode_state
    ADD COLUMN policy_version INTEGER NULL;

ALTER TABLE operational_mode_state
    ADD COLUMN policy_digest TEXT NULL;
```

Semantics:

- `control_session_key` binds WORK/GAME commit to the OS-derived control-session identity that activated it;
- `activation_epoch_id` changes when a fresh managed activation epoch begins;
- policy fields identify the policy whose mandatory target was verified at commit;
- `NONE` may have null policy fields or identify BASE policy according to implementation convention, but the convention must be consistent.

A fresh Windows logon does not reuse the old `activation_epoch_id` as authority for automatic mode restoration.

---

## 4. `mode_transition` extension

The base table is extended with:

```text
operation_kind
requested_target_mode
policy_id
policy_version
policy_digest
control_session_key
initiator_type
initiator_id
original_intent_ref
failure_code
rollback_result_code
```

Allowed `operation_kind`:

```text
ACTIVATE
SWITCH
DEACTIVATE
```

Allowed target mode becomes:

```text
NONE
WORK
GAME
```

Tuple validation is domain-enforced:

```text
ACTIVATE:   NONE → WORK|GAME
SWITCH:     WORK ↔ GAME
DEACTIVATE: WORK|GAME → NONE
```

`source_mode` is the canonical committed mode at operation creation.

`requested_target_mode` MUST NOT be rewritten later to hide a fallback. A fallback outcome is represented separately.

---

## 5. Transition stage codes

`transition_state` uses canonical lifecycle states.

`stage_code` is an implementation-resume discriminator and uses this v1 set:

```text
ACCEPTED
INSPECTION_STARTED
INSPECTION_COMPLETE
WAITING_FOR_USER
RESOLUTION_STARTED
ACTION_PLAN_READY
APPLY_STARTED
APPLY_COMPLETE
VERIFY_STARTED
VERIFY_COMPLETE
COMMIT_STARTED
COMMIT_DURABLE
FINALIZATION_STARTED
ROLLBACK_STARTED
ROLLBACK_VERIFY
TERMINAL
```

Rules:

- stage advancement is monotonic except the explicit transition into rollback stages;
- state/stage pair must be validated by ModeTransition repository;
- `COMMIT_DURABLE` requires `commit_durable=1`;
- `COMPLETED` requires `commit_durable=1` unless the operation is a `NO_OP` that did not create a transition row.

---

## 6. Blocker table

```sql
CREATE TABLE mode_transition_blocker (
    blocker_id               TEXT PRIMARY KEY,
    transition_id            TEXT NOT NULL,
    provider_id              TEXT NOT NULL,
    blocker_code             TEXT NOT NULL,
    blocker_class            TEXT NOT NULL,
    subject_ref              TEXT NULL,
    message_key              TEXT NOT NULL,
    decision_schema_version  INTEGER NULL,
    decision_options_json    TEXT NULL,
    status                   TEXT NOT NULL,
    selected_decision_code   TEXT NULL,
    created_utc              TEXT NOT NULL,
    resolved_utc             TEXT NULL,
    evidence_digest          TEXT NULL,
    FOREIGN KEY(transition_id) REFERENCES mode_transition(transition_id)
        ON DELETE CASCADE
);
```

Allowed classes:

```text
NON_BLOCKING
AUTO_RESOLVABLE
USER_DECISION_REQUIRED
HARD_BLOCK
```

Allowed statuses:

```text
OBSERVED
RESOLVING
WAITING_USER
RESOLVED
WAIVED_BY_POLICY
CANCELLED
STALE
```

`WAIVED_BY_POLICY` is allowed only where the owning policy explicitly permits waiver; a hard safety blocker cannot be silently waived.

---

## 7. Action journal

```sql
CREATE TABLE mode_transition_action (
    action_id                TEXT PRIMARY KEY,
    transition_id            TEXT NOT NULL,
    sequence_no              INTEGER NOT NULL,
    owning_module            TEXT NOT NULL,
    action_type              TEXT NOT NULL,
    target_ref               TEXT NULL,
    desired_schema_version   INTEGER NOT NULL,
    desired_state_json       TEXT NULL,
    desired_state_digest     TEXT NOT NULL,
    mandatory                INTEGER NOT NULL CHECK(mandatory IN (0,1)),
    rollback_class           TEXT NOT NULL,
    verification_class       TEXT NOT NULL,
    action_state             TEXT NOT NULL,
    pre_state_json           TEXT NULL,
    pre_state_digest         TEXT NULL,
    apply_result_code        TEXT NULL,
    verify_result_code       TEXT NULL,
    rollback_result_code     TEXT NULL,
    started_utc              TEXT NULL,
    applied_utc              TEXT NULL,
    verified_utc             TEXT NULL,
    updated_utc              TEXT NOT NULL,
    revision                 INTEGER NOT NULL CHECK(revision >= 1),
    FOREIGN KEY(transition_id) REFERENCES mode_transition(transition_id)
        ON DELETE CASCADE,
    UNIQUE(transition_id, sequence_no)
);
```

Allowed action states:

```text
PLANNED
APPLYING
APPLIED
VERIFYING
VERIFIED
FAILED
ROLLING_BACK
ROLLED_BACK
ROLLBACK_FAILED
SKIPPED
```

`pre_state_json` is permitted only for bounded domain-specific data needed for rollback/reconciliation. It MUST NOT contain arbitrary secrets or document contents.

---

## 8. Major mutation lease

Mode Transition, Update and Recovery share one machine-wide mutation exclusion primitive.

```sql
CREATE TABLE machine_mutation_lease (
    singleton_id            INTEGER PRIMARY KEY CHECK(singleton_id = 1),
    lease_id                TEXT NULL,
    mutation_type           TEXT NULL,
    owner_operation_id      TEXT NULL,
    owner_correlation_id    TEXT NULL,
    owner_control_session_key TEXT NULL,
    fence_token             INTEGER NOT NULL DEFAULT 0,
    acquired_utc            TEXT NULL,
    heartbeat_utc           TEXT NULL,
    expires_utc             TEXT NULL,
    revision                INTEGER NOT NULL CHECK(revision >= 1)
);
```

Allowed `mutation_type` initially:

```text
MODE
UPDATE
RECOVERY
```

### 8.1 Fencing token

Every successful lease acquisition increments `fence_token` monotonically.

All protected machine mutation repository calls that depend on the lease carry the current fencing token.

A stale Runtime Host holding an older token MUST NOT be able to continue machine mutation after a newer owner has acquired the lease.

### 8.2 Lease expiry

Lease timeout is a crash-recovery mechanism, not an authority bypass.

Expiration alone does not prove it is safe to mutate the machine. New ownership requires:

```text
expired/stale lease
→ persisted transaction reconciliation
→ actual-state reconciliation where required
→ new lease acquisition
```

Exact heartbeat interval and timeout numeric values remain implementation-tunable but must be bounded and covered by verification tests.

---

## 9. Repository operations

Mode repository extends SPEC-03 conceptual contract with:

```text
GetModeRuntimeSnapshot()
TryAcquireMajorMutationLease(...)
RenewMajorMutationLease(...)
ReleaseMajorMutationLease(...)
CreateModeOperation(...)
AppendBlockers(...)
RecordBlockerDecision(...)
PersistActionPlan(...)
AdvanceAction(...)
AdvanceModeOperation(...)
CommitTransitionAndMode(...)
MarkModeOperationTerminal(...)
GetIncompleteModeOperations()
```

Machine writes continue through typed `Machine.StateStore.*` Broker capabilities.

No SQL or table names cross IPC.

---

## 10. Atomic boundaries

The following are required atomic transactions:

### 10.1 Lease acquire

Check current lease + create new lease/fence token in one transaction.

### 10.2 Transition creation

Create transition + bind operation identifiers + current source revision atomically enough that duplicate creation cannot produce two active operations.

### 10.3 Commit transition and mode

In one SQLite transaction:

```text
expected source mode revision matches
active transition still owns current lease fence
mandatory verification already recorded
→ update operational_mode_state
→ set commit_durable=1
→ advance transition to durable commit marker
```

### 10.4 Terminalization

Terminal row update and lease release may be separate transactions, but startup reconciliation must tolerate a terminal transition whose lease was not yet cleared.

---

## 11. Active transition uniqueness

The semantic invariant is enforced by the major mutation lease and repository logic.

The implementation MAY also add a partial/derived SQLite uniqueness mechanism if supported cleanly, but DB-only locking is not sufficient authority.

---

## 12. Revision behavior

Mode state and transition records use optimistic revision checks.

Any unexpected revision conflict is treated as concurrency/integrity evidence:

```text
REVISION_CONFLICT
→ stop current semantic mutation
→ reread coherent snapshot
→ reconcile
```

Code MUST NOT perform blind `last write wins` on canonical mode state.

---

## 13. Retention

Completed transition rows and action/blocker detail are diagnostic/recovery evidence.

Initial policy:

- active/incomplete operations: never pruned;
- most recent successful/failed operations: retained according to SPEC-13 diagnostic retention;
- pruning MUST NOT remove evidence referenced by active Recovery/Update records;
- exact count/time retention belongs SPEC-13.

---

## 14. Corruption behavior

If mode state or incomplete transition records fail integrity validation:

```text
unsafe mode mutation blocked
→ Runtime marks managed runtime degraded
→ Recovery required where coherent canonical truth cannot be reconstructed
→ base Windows usability prioritized
```

Corrupt state MUST NOT be replaced with guessed `WORK`, `GAME`, or `NONE` merely to continue.

---

## 15. Acceptance criteria

- physical schema can distinguish ACTIVATE/SWITCH/DEACTIVATE;
- target `NONE` is supported only through valid DEACTIVATE semantics;
- blockers and user decisions are durable enough for restart handling;
- applied actions can be distinguished from planned/unverified actions;
- one shared mutation lease coordinates Mode/Update/Recovery;
- fencing prevents stale process continuation;
- commit mode + durable target boundary is atomic;
- revision conflict never becomes blind overwrite;
- persisted state is sufficient to drive startup reconciliation without guessing.
