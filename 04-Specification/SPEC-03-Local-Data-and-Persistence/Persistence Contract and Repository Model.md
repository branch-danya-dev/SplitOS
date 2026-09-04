# SPEC-03 — Persistence Contract & Repository Model

## 1. Purpose

Defines the contract between semantic owners and physical SQLite persistence.

The implementation may use classes/interfaces in any supported language/framework, but the semantic operations below are normative.

---

## 2. Layering

```text
Semantic owner module
↓
Typed persistence repository/gateway
↓
SQLite provider / Broker persistence capability
↓
physical database
```

Forbidden:

```text
UI → SQL
semantic owner → arbitrary SQL
RuntimeHost → Broker SQL string
cache repository → canonical DB writes
```

---

## 3. Common persistence result

Every write result must distinguish at least:

```text
COMMITTED
REVISION_CONFLICT
VALIDATION_FAILED
SCHEMA_INCOMPATIBLE
PERSISTENCE_BUSY
DISK_FULL
READ_ONLY
IO_FAILURE
CORRUPT
UNAVAILABLE
UNKNOWN_FAILURE
```

`COMMITTED` means the persistence transaction committed. It does not mean the higher-level semantic target was proven unless the repository call itself is the final owner-approved commit boundary.

---

## 4. Common write metadata

Canonical writes carry:

```text
operationId
correlationId where available
expectedRevision where applicable
updatedUtc
schemaVersion
```

Machine Broker writes additionally carry IPC `requestId` and `idempotencyKey` according to SPEC-02.

---

## 5. Mode state repository

Conceptual contract:

```text
GetCommittedMode()
CommitMode(expectedRevision, newMode, operationId, correlationId)
```

`CommitMode` MUST NOT be callable by arbitrary modules.

Only the Mode State owner path may authorize this write.

The repository validates physical constraints, not transition business rules.

---

## 6. Mode transition repository

Conceptual contract:

```text
CreateTransition(record)
GetTransition(transitionId)
GetIncompleteTransitions()
AdvanceTransition(expectedRevision, stage/state delta)
CommitTransitionAndMode(...)
MarkTerminal(...)
```

`CommitTransitionAndMode` SHOULD use one `machine.db` SQLite transaction so the final transition boundary and committed mode cannot diverge physically.

Exact transition state payload comes from SPEC-05.

---

## 7. Installation/baseline repository

Conceptual contract:

```text
GetInstallationIdentity()
GetInstalledBaseline()
CommitVerifiedBaseline(expectedRevision, verifiedBaseline, updateOperationId)
```

`CommitVerifiedBaseline` MUST require an owner-approved verified update/build context.

Persistence implementation cannot infer success from target files existing.

---

## 8. Update repository

Conceptual contract:

```text
CreateUpdateTransaction(...)
GetUpdateTransaction(id)
GetIncompleteUpdateTransactions()
AdvanceUpdateTransaction(...)
MarkRebootRequested(...)
MarkTargetVerified(...)
```

Actual update state machine belongs to SPEC-11.

---

## 9. Recovery repository

Conceptual contract:

```text
CreateRecoveryContext(...)
GetRecoveryContext(id)
GetIncompleteRecoveryContexts()
AdvanceRecoveryAttempt(...)
MarkRecoveryVerified(...)
```

A persistence row saying `verified_result=1` may only be written through the Recovery owner after verification.

---

## 10. Game Profile repository

Conceptual contract:

```text
GetProfiles(gameId)
GetProfile(profileId)
CreateProfile(profile)
UpdateProfile(expectedRevision, profile)
DeleteProfile(expectedRevision, profileId)
SetDefaultProfile(gameId, profileId)
```

Changing default profile plus clearing a previous default SHOULD be one user DB transaction.

Profile validation belongs to Game Profiles / SPEC-08.

---

## 11. User preference repository

Conceptual contract:

```text
GetPreference(owningModule, key)
SetPreference(owningModule, key, expectedRevision, validatedValue)
DeletePreference(...)
```

Repository MUST reject keys whose release-defined owning module mapping does not match the caller contract.

---

## 12. Projection repository

Conceptual contract:

```text
ReplaceClientProjection(sourceClient, observationBatch)
GetGameInstallations(gameId, freshnessRequirement)
InvalidateSource(sourceClient)
DeleteExpired(cutoff)
Rebuild()
```

Projection reads return freshness/provenance with data.

A consumer cannot request `just give me installed=true` without source/freshness metadata when correctness depends on it.

---

## 13. Machine Broker persistence contract

Because `machine.db` is Broker-mediated, Runtime Host machine repositories internally map to bounded SPEC-02 extension capabilities.

### Read

```text
Machine.StateStore.Read@1
```

Request concept:

```json
{
  "recordKind": "OPERATIONAL_MODE",
  "recordId": "singleton"
}
```

Response:

```text
record
revision
schemaVersion
readUtc
```

### Apply

```text
Machine.StateStore.Apply@1
```

Request concept:

```json
{
  "recordKind": "MODE_TRANSITION",
  "recordId": "...",
  "expectedRevision": 4,
  "schemaVersion": 1,
  "mutationType": "ADVANCE",
  "payload": { "...typed fields...": "..." }
}
```

Allowed `recordKind`/`mutationType` combinations are compiled/release-defined.

No SQL/table/file path is accepted.

### Migration

```text
Machine.StateStore.Migrate@1
```

Accepts only a trusted release `migrationId`, not migration SQL.

---

## 14. Broker persistence authorization

Machine persistence capabilities follow SPEC-02 caller validation.

Mutation additionally requires:

```text
valid Runtime Host
+
current physical-console control ownership when operation is user-session initiated
+
record-kind capability allowed
+
schema compatible
+
revision/idempotency valid
```

Maintenance/recovery startup flows without an interactive console are handled only by explicit SPEC-11 machine-owned resume rules; ordinary Broker authorization MUST NOT be weakened globally to support them.

---

## 15. Atomic compound operations

Persistence gateway MAY expose compound operations only when they correspond to a real semantic atomic boundary.

Good:

```text
CommitTransitionAndMode
SetDefaultProfile
CommitVerifiedBaselineAndFinishUpdate
```

Potentially bad:

```text
ExecuteBatch(List<SqlStatement>)
SaveEverything(Dictionary<string,object>)
```

A compound operation must have a named semantic reason and defined owner.

---

## 16. Retry behavior

Repositories do not perform unbounded hidden retries.

Permitted:

- short SQLite busy handling;
- one provider-level transient retry where it cannot duplicate a mutation;
- idempotent Broker retry using the same key.

Semantic retry/backoff belongs to the owning operation.

---

## 17. Cancellation

A caller cancellation before SQLite transaction commit may abort the write where provider semantics allow.

After commit:

```text
caller cancellation
!= rollback committed canonical state
```

The semantic owner must issue a new compensating/rollback operation if required.

---

## 18. Read snapshots

A multi-query read that must represent one coherent view SHOULD use one SQLite read transaction/snapshot.

Example:

```text
OperationalModeState
+
incomplete transition
```

should not be assembled from unrelated reads if a concurrent commit could make the combination impossible.

---

## 19. Error information

Persistence errors exposed upward SHOULD include:

```text
errorClass
recordKind
operationId
retryability
storageRole: MACHINE | USER | PROJECTION
providerCode (diagnostic only)
```

Raw database path/schema/SQL details SHOULD not be shown directly in user UX.

Secrets MUST never be embedded in error payloads.

---

## 20. Acceptance criteria

- semantic modules have typed repositories, not raw connection access;
- compound operations exist only for true semantic atomic boundaries;
- machine repositories cross Broker without SQL tunneling;
- revision conflict is first-class;
- projection reads retain source/freshness;
- cancellation after commit does not pretend the write disappeared;
- persistence result is distinct from higher-level semantic result.
