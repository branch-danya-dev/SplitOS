# SPEC-03 — Durability, Migration & Corruption Model

## 1. Purpose

Defines how SplitOS local databases survive application crash, OS restart, power interruption, schema migration and corruption.

Core rule:

```text
persistence failure
!= permission to fabricate semantic success
```

---

## 2. Durability classes

### D0 — Ephemeral

May be lost on process exit.

Examples:

```text
in-memory hardware observations
transient UI state
```

### D1 — Rebuildable cache

May lose recent state or be deleted/rebuilt.

Examples:

```text
projection.db
Game Client installation projections
short-lived device cache
```

### D2 — User canonical

Must survive normal process/OS restart and should survive power loss according to SQLite canonical durability baseline.

Examples:

```text
Game Profiles
user preferences
Windows-user/account association metadata
```

### D3 — Machine canonical / transaction durable

Must be persisted before ambiguity-producing machine mutation and survive reboot/power loss under the supported storage environment.

Examples:

```text
OperationalModeState
ModeTransitionRecord
UpdateTransactionRecord
RecoveryContext
InstalledBaselineIdentity
```

---

## 3. Canonical SQLite durability

For D2/D3 stores:

```text
journal_mode = WAL
synchronous = FULL
```

The implementation MUST fail initialization/reconciliation if the canonical durability configuration cannot be established/verified and the affected operation requires D2/D3 guarantees.

It MUST NOT silently continue with `synchronous=OFF` or an unknown durability mode.

---

## 4. Transaction commit semantics

For canonical data:

```text
SQLite COMMIT success
→ physical persistence accepted by storage layer
→ semantic owner may continue
```

But:

```text
SQLite COMMIT success
!= external Windows target verified
!= Work/Game semantic commit unless the semantic prerequisites are already satisfied
```

The transaction must occur at the correct semantic boundary defined by the owning state machine.

---

## 5. Write ordering for machine mutations

When crash/reboot reconciliation requires knowing what operation was attempted:

```text
1. persist operation identity/source/target
2. commit journal state
3. begin ambiguous machine mutation
4. persist progress at required durable checkpoints
5. observe actual result
6. verify semantic target
7. atomically persist semantic commit/terminal transaction result
8. publish success
```

A machine mutation MUST NOT intentionally occur before required journal durability.

---

## 6. WAL checkpoints

Checkpointing is a storage-maintenance action, not a product semantic state transition.

Rules:

- allow SQLite automatic checkpointing as baseline unless measurements justify tuning;
- a checkpoint failure is persistence health evidence and must be observable;
- do not equate checkpoint completion with transaction semantic success;
- maintenance MAY request explicit checkpoint before backup/migration/shutdown when appropriate;
- do not aggressively checkpoint merely to make the `-wal` file disappear.

Exact checkpoint thresholds are implementation/performance tuning, not product truth.

---

## 7. Graceful shutdown

Owning process SHOULD:

1. stop new persistence operations;
2. complete/abort local DB transactions;
3. flush required semantic writes;
4. optionally checkpoint where operational policy requires;
5. close DB connections cleanly.

An OS shutdown timeout or process crash does not authorize marking an unfinished semantic operation complete.

---

## 8. Unclean startup detection

Each owning persistence process SHOULD maintain a technical clean-shutdown marker or equivalent startup evidence.

On unclean startup:

```text
canonical DB
→ open safely
→ quick integrity check / provider recovery behavior
→ load durable semantic records
→ reconcile actual system state
```

The marker is diagnostic/recovery evidence, not canonical mode state.

---

## 9. Integrity checks

### Fast check

Use SQLite `PRAGMA quick_check` for startup/recovery validation when an unclean exit, migration or suspicious storage error justifies it.

### Full check

Use:

```sql
PRAGMA integrity_check;
PRAGMA foreign_key_check;
```

for deeper repair/support/recovery paths.

A successful `quick_check` does not replace all higher-level semantic invariants.

Example:

```text
DB structurally valid
but
OperationalModeState says GAME
while incomplete transition journal proves commit never occurred
```

is a semantic reconciliation problem, not SQLite corruption.

---

## 10. Corruption classification

### C1 — projection cache corruption

Impact:

```text
rebuildable
```

Response:

```text
close DB
→ quarantine/delete projection DB
→ recreate schema
→ rescan external sources
```

No Recovery mode is required solely because projection cache was discarded.

### C2 — user canonical DB corruption

Impact:

```text
potential loss of Game Profiles/preferences/account association metadata
```

Response:

```text
stop writes
→ preserve/quarantine corrupt DB
→ inspect verified backup
→ restore backup if valid
→ migrate/validate
→ otherwise expose controlled reset/import/support path
```

Do not fabricate missing Game Profiles.

### C3 — machine canonical DB corruption

Impact:

```text
committed mode/update/recovery truth unavailable or ambiguous
```

Response:

```text
block new managed machine mutations
→ preserve base Windows usability
→ enter persistence recovery path
→ validate machine backup/release evidence/actual Windows state
→ restore/repair only through Recovery ownership
```

If canonical machine truth cannot be proven, SplitOS MUST NOT claim a successful WORK/GAME/update state merely to recover UX.

---

## 11. Quarantine rule

A corrupt canonical DB SHOULD be moved/copied to a protected quarantine location before destructive rebuild where feasible.

Quarantine artifact name SHOULD include:

```text
database role
UTC timestamp
failure/correlation reference
```

It may contain sensitive user/system metadata and therefore retains original ACL protection.

---

## 12. Backup mechanism

Live SQLite DB backups MUST use a SQLite-consistent mechanism such as Online Backup API/provider equivalent.

Do not copy only the main `.db` file while WAL transactions may be active.

Backup result MUST itself be opened/validated before being advertised as usable recovery evidence.

---

## 13. Backup rotation baseline

### `user.db`

At minimum create a backup:

- before schema migration;
- before a destructive profile/data migration;
- on explicit support/export operation where required.

### `machine.db`

Create a protected backup:

- before machine schema migration;
- before an update/recovery operation that changes persisted schema/critical metadata if recovery design requires it.

### `projection.db`

No backup required.

Exact generations/retention count is deferred to operational tuning/SPEC-13, but v1 SHOULD keep more than one recent valid canonical backup where disk constraints permit.

---

## 14. Schema versioning

Each DB has a monotonic integer physical schema version.

```text
schema v1
→ migration 1→2
→ schema v2
```

A release MUST ship an explicit supported migration chain for every database version it claims to upgrade.

Runtime MUST NOT attempt to infer a migration from table shape.

---

## 15. Migration ownership

### User DB migration

Executor:

```text
Runtime Host persistence infrastructure
```

under current Windows user identity.

### Machine DB migration

Executor:

```text
Privileged Broker machine persistence infrastructure
```

through a release-defined migration ID/capability.

Runtime Host MUST NOT send arbitrary SQL migration text to Broker.

---

## 16. Machine migration contract

`Machine.StateStore.Migrate@1` input concept:

```text
migrationId
sourceSchemaVersion
targetSchemaVersion
expectedReleaseId
operationId
idempotencyKey
```

Broker resolves `migrationId` to a migration bundled with/trusted by the installed release.

Raw SQL from IPC is prohibited.

Sequence:

```text
validate release/migration
→ backup machine.db
→ validate backup
→ execute migration transaction(s)
→ set user_version + schema_metadata
→ quick_check + foreign_key_check
→ commit migration result
```

If validation fails:

```text
migration = FAILED
```

and the product enters compatibility/recovery handling rather than continuing with unknown schema.

---

## 17. Migration atomicity

A migration SHOULD execute within one SQLite transaction where technically possible.

If a migration requires multiple phases that cannot be one transaction:

- it must have a durable migration journal/state;
- every phase must be restart-safe/idempotent;
- the product must not expose the DB as current schema until final verification succeeds.

Such multi-phase migrations require explicit review.

---

## 18. Downgrade behavior

Automatic schema downgrade is not assumed supported.

If older SplitOS code encounters a newer DB schema it does not explicitly support:

```text
NEWER_SCHEMA_UNSUPPORTED
```

It MUST NOT attempt best-effort writes.

Rollback to an older release therefore requires an explicit compatible data downgrade/restore policy from Update/Recovery specification.

---

## 19. Forward-compatible payloads

Versioned JSON payloads may support additive fields, but only when the owning module declares compatibility.

Unknown fields MUST NOT automatically be ignored if they affect safety/security semantics.

General rule:

```text
unknown optional presentation field
→ may ignore if contract permits

unknown security/mode/update behavior field
→ fail compatibility validation
```

---

## 20. Concurrent access / busy behavior

SQLite write contention returns busy/locked outcomes rather than justifying concurrent semantic writers.

`busy_timeout` is a short technical wait, not an ownership arbitration mechanism.

If contention persists:

```text
PERSISTENCE_BUSY
```

is surfaced to the owning module.

The semantic operation decides retry/defer/fail according to its failure model.

---

## 21. Optimistic revision conflict

For canonical mutable records:

```text
expectedRevision != storedRevision
→ REVISION_CONFLICT
```

Resolution:

```text
reload current canonical record
→ reevaluate intent
→ retry as a new owner-approved write if still valid
```

Never force overwrite merely because the UI submitted newer wall-clock time.

---

## 22. Disk-full / IO failure

SQLite errors such as disk full, readonly storage, I/O error or inability to sync canonical writes map to a persistence failure.

For D3 operations:

```text
required persistence failed
→ machine mutation/semantic commit must not advance beyond safe boundary
```

For projection cache:

```text
cache write failed
→ may continue without cache if safe
```

---

## 23. Backup restore verification

A backup is usable only after:

```text
open successfully
→ expected DB role/schema
→ quick_check/integrity validation as required
→ foreign_key_check
→ release/schema compatibility
→ semantic owner reconciliation
```

Restoring an old but structurally valid backup does not automatically make its `OperationalModeState` equal actual Windows state.

---

## 24. User reset behavior

Deleting/resetting `user.db` is a destructive user-data action.

It requires explicit UX/repair flow and must disclose loss of local:

- Game Profiles;
- user preferences;
- Shared App preferences;
- local account association metadata.

It does not delete the backend SplitOS Account itself.

---

## 25. Machine reset behavior

Deleting/recreating `machine.db` is not a normal troubleshooting shortcut.

It requires Recovery ownership because it can destroy evidence needed to distinguish:

```text
last committed mode
incomplete transition
incomplete update
active recovery
installed baseline identity
```

If a full machine-state rebuild is necessary, canonical values must be reconstructed from verified release/actual-state/recovery evidence, not defaults disguised as truth.

---

## 26. Acceptance criteria

Implementation passes this model only if tests demonstrate:

1. canonical DB commit survives Runtime Host process crash;
2. WAL+FULL canonical transaction survives supported crash/power-loss testing expectations;
3. projection DB can be rebuilt after deletion/corruption;
4. machine/user DB corruption does not fabricate semantic state;
5. backup taken while DB is live is SQLite-consistent;
6. user and machine migrations create/validate backup before destructive migration;
7. newer unsupported schema fails closed for writes;
8. machine migration cannot receive arbitrary SQL from Runtime Host;
9. revision conflict is detected and not silently overwritten;
10. disk-full/IO failure blocks required durable semantic commit;
11. incomplete transition/update/recovery remains discoverable after restart.

---

## 27. Engineering evidence

Relevant SQLite mechanisms:

- `PRAGMA synchronous`, `quick_check`, `integrity_check`, `foreign_key_check`: `https://sqlite.org/pragma.html`
- WAL: `https://www.sqlite.org/wal.html`
- Online Backup API: `https://www.sqlite.org/backup.html`
- Atomic commit model: `https://www.sqlite.org/atomiccommit.html`
