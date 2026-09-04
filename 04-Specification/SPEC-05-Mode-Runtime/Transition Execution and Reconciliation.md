# SPEC-05 — Transition Execution and Reconciliation

## 1. Purpose

Defines the concrete execution algorithm for mode operations and the rules used after Runtime crash, Windows reboot, logoff, control-session change or partial application.

---

## 2. Operation start

### 2.1 Build request context

Runtime derives:

```text
operationId
correlationId
transitionId
operationKind
sourceMode
targetMode
current controlSessionKey
current OperationalMode revision
runtime access decision
```

### 2.2 Acquire major mutation lease

Before creating target-changing work:

```text
TryAcquireMajorMutationLease(MODE, operationId, controlSessionKey)
```

Failure results:

```text
lease held by MODE
→ ALREADY_IN_PROGRESS/BUSY

lease held by UPDATE
→ BUSY_UPDATE

lease held by RECOVERY
→ RECOVERY_REQUIRED/BUSY_RECOVERY
```

No target mutation happens without a valid current fence token.

### 2.3 Create durable operation record

After lease acquisition:

```text
CreateModeOperation(...)
→ state REQUESTED
→ stage ACCEPTED
```

If persistence fails, the lease is released where safe and the operation is not accepted as started.

---

## 3. Inspection algorithm

```text
REQUESTED
→ INSPECTING
→ call registered providers
→ persist blocker observations
```

Decision:

```text
any HARD_BLOCK
→ BLOCKED

any USER_DECISION_REQUIRED
→ AWAITING_USER

AUTO_RESOLVABLE present
→ RESOLVING

none
→ RESOLVING / build target plan
```

A transition cannot enter APPLYING with an unresolved `HARD_BLOCK` or `USER_DECISION_REQUIRED` blocker.

---

## 4. User decision resume

When Runtime receives a decision:

1. validate request belongs to current active transition;
2. validate blocker is still current;
3. persist selected closed decision code;
4. invoke owning provider resolution;
5. re-observe/revalidate affected evidence;
6. continue only when blocker is resolved.

If Runtime Host crashes while waiting, same-session restart MAY resurface the pending decision after validating freshness.

A fresh Windows logon MUST NOT blindly replay an old user confirmation; stale prior-session decisions are cancelled/re-evaluated.

---

## 5. Resolve target policy

ModePolicy builds an immutable effective target snapshot.

Persist before mutation:

```text
policyId
policyVersion
policyDigest
resolved target selectors/fallback identity
```

If required policy cannot be resolved:

```text
no mutations yet
→ CANCELLED or FAILED_WITH_SAFE_FALLBACK depending source coherence
```

---

## 6. Build action plan

Each owning module contributes typed actions.

Conceptual order groups:

```text
G10 PREPARE_USER_CONTEXT
G20 APPLICATION_LIFECYCLE
G30 USER_SESSION_CONTEXT
G40 MACHINE_MANAGED_COMPONENTS
G50 TARGET_RUNTIME_READINESS
G60 TARGET_UX_READINESS
```

Within a group, actions have deterministic `sequence_no`.

Ordering may be refined by SPEC-06/07/09, but it MUST preserve declared dependencies.

The full plan is persisted before first mutating action.

---

## 7. Apply algorithm

For each action in order:

```text
check current lease fence
→ mark APPLYING
→ capture bounded pre-state if rollback requires it
→ invoke owning adapter/Broker capability
→ record immediate result
→ mark APPLIED or FAILED
```

Rules:

- a mandatory action failure stops forward apply;
- an optional action failure may continue only when policy permits;
- an action timeout is `UNKNOWN/FAILED` until actual-state reconciliation determines outcome;
- no caller assumes timeout means action did not happen.

---

## 8. Verify algorithm

After apply:

```text
transition → VERIFYING
```

For each mandatory action/invariant:

```text
read actual evidence
→ semantic verifier
→ VERIFIED / MISMATCH / UNKNOWN
```

Target commit allowed only when:

```text
all mandatory invariants = VERIFIED
```

`UNKNOWN` for a mandatory invariant behaves as failed verification until resolved.

---

## 9. Commit algorithm

Before commit:

```text
lease fence still current
source committed-mode revision unchanged
runtime access still permits target (except DEACTIVATE)
policy identity still active/compatible
mandatory verification persisted
```

Then:

```text
transition = COMMITTING
stage = COMMIT_STARTED
```

Perform atomic repository operation:

```text
CommitTransitionAndMode(
  expectedModeRevision,
  transitionRevision,
  targetMode,
  controlSessionKey,
  activationEpochId,
  policyIdentity,
  fenceToken
)
```

On `COMMITTED`:

```text
commit_durable = 1
stage = COMMIT_DURABLE
OperationalMode = target
```

At this instant the semantic mode truth changes.

---

## 10. Finalization

Post-commit tasks may include:

- foreground Game Launcher presentation;
- UI state refresh;
- non-critical cleanup;
- release of old transient handles;
- final diagnostics.

Failure during finalization MUST NOT revert target canonical mode automatically.

Runtime reconciles/finalizes around the committed target.

After sufficient finalization:

```text
transition = COMPLETED
stage = TERMINAL
release major mutation lease
```

---

## 11. Rollback trigger

Rollback begins if target cannot be safely committed after any mutation has occurred.

Examples:

```text
mandatory apply failure
mandatory verification failure
user cancellation after mutation
entitlement loss before premium target commit
control ownership lost during operation
stale lease/fencing conflict
required device disappears after action plan
```

Transition:

```text
→ ROLLING_BACK
→ stage ROLLBACK_STARTED
```

---

## 12. Rollback action order

Rollback normally walks applied actions in reverse dependency/order.

For each action:

```text
REVERSIBLE
→ apply captured/derived source state

RECONCILABLE
→ ask owning module to resolve source-policy actual state

NON_REVERSIBLE
→ cannot claim normal rollback; Recovery/fallback likely required
```

The rollback objective is **not** “undo every API call byte-for-byte”.

Objective:

```text
establish verified coherent source policy or BASE safe state
```

---

## 13. Rollback verification

After compensation:

```text
stage = ROLLBACK_VERIFY
```

If source mode is still authorized and source target verifies:

```text
committed mode remains source
→ terminal CANCELLED for user/precondition cancellation
or
→ terminal FAILED_WITH_SAFE_FALLBACK for technical failure with restored source
```

If source managed mode is no longer authorized:

```text
verify BASE target
→ DEACTIVATE/convergence semantics
→ committed mode NONE when atomic commit succeeds
```

If no coherent source/BASE state can be proven:

```text
→ Recovery
```

---

## 14. Runtime Host crash reconciliation

On same-logon Runtime Host restart:

### Case A — no incomplete mode operation

```text
read committed mode
read current controlSessionKey
if key matches and mode WORK/GAME
→ verify/reconcile actual target policy
→ continue managed mode
```

### Case B — incomplete, `commit_durable=0`

```text
source mode still canonical
→ inspect persisted action journal
→ read actual state
→ rollback/reconcile source
→ mark terminal or escalate Recovery
```

### Case C — incomplete, `commit_durable=1`

```text
target mode canonical
→ verify/reconcile target
→ finish COMPLETED if coherent
→ otherwise Recovery/fallback around target truth
```

Runtime MUST NOT reset target to source merely because `COMPLETED` was not written.

---

## 15. Broker crash / IPC unknown result

If Runtime loses Broker response after issuing a mutation:

```text
response missing
!= operation not executed
```

Runtime uses:

- same idempotency key on safe retry;
- persisted machine idempotency ledger;
- actual-state read-back;
- action journal.

Only after reconciliation may it mark the action APPLIED/FAILED/UNKNOWN.

Unknown mandatory mutation prevents target commit.

---

## 16. Fresh Windows logon / reboot

A new physical-console logon/control-session identity starts a new activation epoch.

Algorithm:

```text
Windows sign-in
→ Runtime Host starts
→ resolve account/access
→ load machine snapshot
→ resolve incomplete Recovery/Update first
→ reconcile incomplete mode operation if present
→ compare persisted controlSessionKey with current key
```

If different:

```text
previous WORK/GAME is prior-session evidence
→ do not auto-reactivate
→ acquire MODE lease if needed
→ reconcile SplitOS-managed deltas to BASE
→ atomically commit NONE
→ release lease
→ if FREE: normal Windows Desktop
→ if PRO: present mode selection
```

This is the v1 startup policy.

---

## 17. Clean logoff/shutdown

Best-effort clean shutdown MAY proactively converge mode to BASE/NONE before user session exits when product timing permits.

However correctness MUST NOT depend on receiving a clean shutdown notification.

Power loss, forced reboot or termination are handled by startup reconciliation.

---

## 18. Control-session switch

If physical console ownership changes from Session A to Session B:

1. Session A Runtime immediately loses authorization for new machine mutations.
2. any in-flight operation checks fence/control ownership before next privileged step;
3. Broker rejects stale/non-console mutation;
4. new console Runtime loads/reconciles machine state;
5. a safe unfinished operation is completed/rolled back/recovered before new mode intent is accepted.

No two sessions can have simultaneous valid mutation ownership.

---

## 19. Entitlement loss during operation

### Before first mutation

```text
cancel operation
→ no rollback
```

### After mutation, before commit

```text
rollback source
→ if source managed mode also no longer authorized
→ converge BASE/NONE
```

### After target commit

The committed target remains truth until a new explicit `DEACTIVATE target→NONE` operation commits.

Runtime denies new premium operations while convergence is pending.

---

## 20. Device topology change during operation

Relevant device evidence is subscribed/invalidated by context owners.

If required target evidence changes before commit:

```text
stop forward progress
→ re-resolve only if still before irreversible mutation and policy permits
or
→ rollback and start a new operation
```

The engine MUST NOT silently patch an already-applied action plan into a materially different target.

---

## 21. Action replay rules

On reconciliation:

- `PLANNED`: never assume executed;
- `APPLYING`: outcome unknown until idempotency/evidence check;
- `APPLIED`: verify actual state before trusting;
- `VERIFIED`: may still be rechecked if evidence has freshness semantics;
- `ROLLING_BACK`: reconcile compensation outcome;
- `ROLLED_BACK`: verify source target as a whole.

Persisted action state is evidence about previous orchestration, not replacement for current actual-state evidence.

---

## 22. Recovery escalation payload

When Mode Runtime escalates to Recovery it provides a bounded context:

```text
transitionId
operationId
correlationId
sourceMode
targetMode
operationKind
commitDurable
policyIdentity
lastStage
failedActionIds
appliedActionIds
rollbackResults
current lease/fence evidence
current controlSession identity
```

It MUST NOT provide Recovery with an instruction to blindly set a mode enum.

---

## 23. Acceptance scenarios

Minimum executable scenarios:

```text
NONE→WORK success
NONE→GAME success
WORK→GAME success
GAME→WORK success
WORK/GAME→NONE entitlement deactivation
preflight user cancel
hard blocker
failure before mutation
failure mid-apply
verification mismatch
cancel after mutation
Runtime crash before commit
Runtime crash after durable commit
Broker timeout after unknown mutation
power loss during APPLYING
power loss during COMMITTING
fresh logon after prior GAME
console session switch mid-transition
entitlement loss mid-transition
device loss mid-transition
rollback failure → Recovery
```
