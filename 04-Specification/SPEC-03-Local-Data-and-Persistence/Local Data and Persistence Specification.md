# SPEC-03 — Local Data & Persistence Specification

Status: **READY FOR REVIEW**  
Scope: physical local storage topology, durability classes, write authority, schema/versioning baseline and corruption behavior.  
Out of scope: account protocol/token lifetime semantics (`SPEC-04`), exact ModeTransition business fields (`SPEC-05`), Game Profile optimization semantics (`SPEC-08`), update/recovery algorithm (`SPEC-11`), diagnostics retention (`SPEC-13`).

---

## 1. Purpose

This specification converts the A&D placement classes into physical Windows storage rules without changing semantic ownership.

Core invariant:

```text
storage location / database writer
!= semantic owner
```

The persistence layer stores owner-approved state. It does not decide mode, entitlement, compatibility, update success or recovery success.

---

## 2. v1 storage engines

SplitOS v1 uses **SQLite** for structured local persistence.

The local persistence baseline consists of three independent databases:

```text
MACHINE CANONICAL
%ProgramData%\SplitOS\Data\machine.db

USER CANONICAL
%LocalAppData%\SplitOS\Data\user.db

REBUILDABLE PROJECTION CACHE
%LocalAppData%\SplitOS\Cache\projection.db
```

Protected account credentials/tokens are **not** stored as plaintext or reusable credential values in any SQLite database.

Diagnostics are physically separate from canonical databases and remain governed by `SPEC-13`.

---

## 3. Why three databases

A single `splitos.db` is prohibited for v1 because it would merge different authority, durability and recovery classes.

The physical split preserves:

```text
machine-wide protected canonical state
!= per-user canonical preferences/profiles
!= external/rebuildable evidence cache
```

Benefits:

- machine state can receive stricter ACL/write mediation;
- cache corruption can be discarded without destroying Game Profiles;
- per-user data naturally follows the Windows user context;
- machine recovery does not depend on one user's profile store;
- canonical durability settings can differ from cache performance settings;
- backup/migration policies can differ by placement class.

---

## 4. Windows folder placement

### 4.1 Machine data root

Logical root:

```text
FOLDERID_ProgramData
→ %ProgramData%\SplitOS\
```

Subdirectories:

```text
%ProgramData%\SplitOS\Data\
%ProgramData%\SplitOS\Backups\
%ProgramData%\SplitOS\Staging\
%ProgramData%\SplitOS\Logs\     # policy finalized in SPEC-13
```

Machine canonical data MUST NOT be placed under a specific user's profile.

### 4.2 User data root

Logical root:

```text
FOLDERID_LocalAppData
→ %LocalAppData%\SplitOS\
```

Subdirectories:

```text
%LocalAppData%\SplitOS\Data\
%LocalAppData%\SplitOS\Cache\
%LocalAppData%\SplitOS\Backups\
%LocalAppData%\SplitOS\Logs\    # policy finalized in SPEC-13
```

The application SHOULD resolve Windows known folders rather than hard-code `C:\Users\...` or `C:\ProgramData` paths.

---

## 5. Database roles

### 5.1 `machine.db`

Placement class:

```text
MACHINE_CANONICAL
+
TRANSACTION_DURABLE
```

Contains only machine-scoped state that must survive Runtime Host crash and, where required, reboot/power interruption.

Initial record families:

```text
SplitOSInstallation
InstalledBaselineIdentity
OperationalModeState
ModeTransitionRecord
UpdateTransactionRecord
RecoveryContext
machine schema metadata
```

`machine.db` MUST NOT contain:

- account refresh/access tokens;
- arbitrary user preferences;
- Game Client raw library dumps;
- diagnostics as a substitute for state;
- raw payment evidence;
- Game Launcher view state.

### 5.2 `user.db`

Placement class:

```text
USER_CANONICAL
```

Contains state owned by the current Windows user's SplitOS experience.

Initial record families:

```text
WindowsUserAccountAssociation metadata
GameProfile
UserPreference
SharedAppPreference
profile/default-selection metadata
user schema metadata
```

The SplitOS account itself and entitlement remain backend-owned.

### 5.3 `projection.db`

Placement class:

```text
PROJECTION_CACHE
```

Contains rebuildable evidence/projections, for example:

```text
Game Client availability
GameInstallationProjection
unified library projection cache
external game IDs / install paths after validation
source/freshness metadata
short-lived hardware/device projection where beneficial
```

Deletion of `projection.db` MUST NOT destroy canonical user configuration.

Runtime MUST be able to rebuild it from authoritative/external evidence.

---

## 6. SQLite journal and synchronous policy

### 6.1 Canonical databases

For `machine.db` and `user.db`:

```sql
PRAGMA journal_mode = WAL;
PRAGMA synchronous = FULL;
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;
```

Rationale:

- WAL provides transactional atomicity plus concurrent readers;
- `FULL` is required for the canonical baseline because committed state such as mode/update/recovery records must not intentionally trade power-loss durability for throughput;
- SplitOS canonical writes are low enough volume that durability is more important than maximum transaction throughput.

A provider implementation MUST verify the resulting pragma values rather than assume unknown/unsupported pragmas succeeded.

### 6.2 Projection cache

For `projection.db`:

```sql
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;
```

Loss of the most recent projection commit after power loss is acceptable because the data is rebuildable and carries freshness semantics.

### 6.3 No `synchronous=OFF`

`OFF` is prohibited for all SplitOS persistent databases.

---

## 7. Connection ownership

### 7.1 User database

`RuntimeHost` for the Windows user is the normal owner of write connections to `user.db` and `projection.db`.

Manager/Game Launcher MUST NOT open these database files directly.

They use Runtime Host semantic contracts.

### 7.2 Machine database

`machine.db` is physically protected machine data.

Normal user processes MUST NOT receive direct write access to the file merely so Runtime Host can persist state.

v1 baseline:

```text
semantic owner in Runtime Host
→ typed Machine State persistence request
→ Broker
→ validate caller/capability/schema/revision
→ SQLite transaction against machine.db
→ technical persistence result
→ semantic owner continues state machine
```

Broker is a persistence executor, not the semantic owner.

### 7.3 Direct read access

Direct read access to `machine.db` by arbitrary normal-user processes is not required.

Runtime Host obtains machine-canonical records through a bounded Broker machine-state read contract or startup/reconciliation contract.

This avoids making protected machine files world-readable/writable by default.

---

## 8. Machine state persistence capability

`SPEC-02` is extended by this package with bounded persistence capabilities:

```text
Machine.StateStore.Read@1
Machine.StateStore.Apply@1
Machine.StateStore.Migrate@1
```

They are not SQL tunneling APIs.

### 8.1 `Machine.StateStore.Read@1`

Allowed record kinds:

```text
INSTALLATION
INSTALLED_BASELINE
OPERATIONAL_MODE
MODE_TRANSITION
UPDATE_TRANSACTION
RECOVERY_CONTEXT
```

Caller supplies typed record identity only.

Raw table names, SQL, file paths and arbitrary predicates are prohibited.

### 8.2 `Machine.StateStore.Apply@1`

Mutation request contains:

```text
recordKind
recordId
schemaVersion
expectedRevision
operationId
correlationId
idempotencyKey
typed payload
```

Broker MUST:

1. validate caller under SPEC-02;
2. validate record kind and schema version;
3. validate payload against the compiled/signed release contract;
4. perform optimistic revision check where applicable;
5. execute one SQLite transaction;
6. return committed revision / conflict / technical failure.

A caller cannot supply arbitrary SQL.

### 8.3 Semantic restrictions

Storage acceptance does not prove semantic correctness.

Example:

```text
Machine.StateStore.Apply(OPERATIONAL_MODE=GAME)
```

MUST only be issued by the owning Mode State path after the transition verification/commit preconditions defined by A&D/SPEC-05.

Broker may enforce structural and trust preconditions but does not replace the Mode State owner.

---

## 9. Canonical revision model

Mutable canonical rows SHOULD expose:

```text
revision INTEGER >= 1
updated_utc
updated_by_operation_id
```

Writes use optimistic comparison:

```text
expectedRevision = N
→ UPDATE ... WHERE revision = N
→ revision = N+1
```

If zero rows are updated:

```text
REVISION_CONFLICT
```

The caller MUST reconcile rather than overwrite blindly.

This is especially important for:

- `OperationalModeState`;
- active transition/update/recovery records;
- Game Profiles edited through more than one UI activation path.

---

## 10. Transaction boundaries

A semantic commit that requires multiple physical row mutations MUST map to one SQLite transaction when all affected records are in the same database.

Example:

```text
final transition result
+
new OperationalModeState
+
transition terminal marker
```

SHOULD be persisted atomically when SPEC-05 finalizes the exact transition schema.

No UI success may be emitted before the required persistence transaction completes where durability is part of semantic commit.

---

## 11. Mode state durability

`OperationalModeState` is machine-canonical in v1.

It MUST survive:

- Runtime Host process crash;
- Game Launcher/Manager crash;
- sign-out/sign-in;
- normal OS reboot.

The persisted value is the **last durably committed operational mode**, not live Windows evidence.

Therefore after startup:

```text
durable committed mode
+
actual Windows state evidence
+
incomplete transaction records
→ reconciliation
```

The durable row alone MUST NOT cause Runtime Host to assume platform policy is currently applied correctly.

---

## 12. Mode transition journal durability

A transition record MUST become durable before the first machine mutation that could leave an ambiguous state after Runtime Host crash.

Minimum durability checkpoints:

```text
transition created
→ source/target + operation identity durable

before APPLYING machine mutations
→ current stage durable

after verification, before/with semantic commit
→ commit boundary durable

terminal outcome
→ durable
```

Exact business states remain defined by `SPEC-05`.

---

## 13. Update and recovery durability

`UpdateTransactionRecord` and `RecoveryContext` live in `machine.db`.

They MUST support continuation across reboot.

Persistence rules:

- transaction identity is durable before privileged apply;
- target release identity is not copied to `InstalledBaselineIdentity` before post-apply verification succeeds;
- reboot-required stage is committed before restart request;
- recovery attempt/result is durable before another recovery strategy is selected where ambiguity would matter.

Exact fields/algorithms remain `SPEC-11`.

---

## 14. User data ownership and access

`user.db` MUST be writable only in the current Windows user's security context plus explicitly required system/service recovery identities.

Other ordinary Windows users MUST NOT receive access by default.

A second Windows account therefore receives its own:

```text
%LocalAppData%\SplitOS\Data\user.db
```

This naturally prevents User B from editing User A's Game Profiles through normal ACL boundaries.

---

## 15. Game Profile physical persistence

`GameProfile` is stored in `user.db`.

Physical schema MUST preserve:

```text
profileId
gameId
profileName
isDefault
profileSchemaVersion
configuration payload / normalized fields
createdUtc
updatedUtc
revision
```

Detailed profile configuration semantics are finalized by `SPEC-08`.

`SPEC-03` requires that profile payload evolution be versioned and migratable; unknown future fields MUST NOT silently change current semantics.

---

## 16. User preference persistence

User preference storage uses bounded preference keys defined by product modules.

An arbitrary global settings bag MUST NOT become a hidden ownership layer.

Each preference entry MUST identify at least:

```text
preferenceKey
owningModule
schemaVersion
value
updatedUtc
revision
```

Owning modules validate values before persistence.

---

## 17. External projections

Every persisted external projection MUST retain provenance/freshness.

Minimum metadata:

```text
sourceType
sourceInstance/client ID where relevant
externalObjectId
observedAtUtc
expiresAtUtc or freshness policy reference
adapterCapabilityStatus
projectionSchemaVersion
```

A projection without freshness/provenance MUST NOT be promoted into canonical decision input when freshness is material.

---

## 18. Protected secrets

Reusable authentication secrets MUST NOT be persisted as normal SQLite fields.

General pattern:

```text
user.db
→ opaque secret reference / metadata only

Protected Secret Store
→ actual reusable credential material
```

Current Windows-native protection candidate remains user-scoped DPAPI from Trust analysis.

Exact token types/container/rotation are defined in `SPEC-04`.

No secret value may be copied into:

- `projection.db`;
- diagnostic logs;
- Broker audit payload;
- Game Launcher/Manager database access.

---

## 19. Release knowledge

Signed Build/Update manifests, Component Matrix and release compatibility metadata are **release artifacts**, not mutable SQLite canonical state.

SQLite may store references/installed identity/hash metadata needed for reconciliation, but it MUST NOT turn a locally editable copy into release authority.

---

## 20. Backup classes

### User canonical

`user.db` SHOULD support local pre-migration backup because Game Profiles/preferences may not be reconstructable.

### Machine canonical

`machine.db` MUST support pre-migration/maintenance backup for recovery purposes.

Backup creation MUST use a SQLite-consistent backup mechanism rather than copying a live DB file without accounting for WAL/journal state.

### Projection cache

`projection.db` does not require durable backup; rebuild is preferred.

---

## 21. Corruption policy summary

```text
projection.db corrupt
→ quarantine/delete
→ rebuild

user.db corrupt
→ stop user-canonical writes
→ attempt verified backup restore/recovery
→ preserve corrupt artifact for support
→ do not fabricate profiles/preferences

machine.db corrupt
→ block unsafe managed mutations
→ enter Recovery / base-Windows-safe path
→ restore/repair from verified machine backup or recovery evidence
→ never invent committed mode/update result
```

Detailed mechanics are in `Durability Migration and Corruption Model.md`.

---

## 22. Database ownership invariant

No module except persistence infrastructure executes arbitrary SQL against shared databases.

Semantic modules use repositories/gateways such as:

```text
IModeStateStore
IModeTransitionStore
IGameProfileStore
IUserPreferenceStore
IGameProjectionStore
IUpdateTransactionStore
IRecoveryContextStore
```

These names are contract concepts, not a mandated programming language interface syntax.

---

## 23. Acceptance criteria

SPEC-03 is satisfied only if implementation demonstrates:

1. machine/user/projection data are physically separated;
2. machine canonical writes cannot be performed by arbitrary normal-user processes;
3. Manager/Game Launcher cannot bypass Runtime Host persistence contracts;
4. canonical databases use WAL + FULL durability baseline;
5. projection cache can be deleted and rebuilt without destroying canonical data;
6. reusable auth secrets are absent from ordinary DB plaintext fields;
7. committed mode survives Runtime Host restart/reboot but is still reconciled with actual state;
8. durable transition/update/recovery records exist before ambiguity-producing operations;
9. migrations are versioned and reversible/recoverable according to migration policy;
10. corruption never causes fabricated canonical state;
11. external projections persist provenance/freshness;
12. direct arbitrary SQL is not exposed over Broker IPC.

---

## 24. Engineering evidence

Storage decisions are based on:

- SQLite WAL documentation: `https://www.sqlite.org/wal.html`
- SQLite `PRAGMA synchronous`: `https://sqlite.org/pragma.html`
- SQLite atomic commit model: `https://www.sqlite.org/atomiccommit.html`
- SQLite Online Backup API: `https://www.sqlite.org/backup.html`
- Windows Known Folder IDs: `https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid`

The specification intentionally uses Windows known-folder semantics rather than assuming fixed absolute paths.