# SPEC-02 Extension — Machine State Persistence Capabilities

Status: **NORMATIVE EXTENSION FROM SPEC-03**

## 1. Purpose

SPEC-03 requires `machine.db` to remain protected from arbitrary normal-user writes while semantic ownership remains in Runtime Host modules.

Therefore the Broker capability surface is extended with three bounded persistence capabilities.

These capabilities do **not** expose SQL, table names, database paths or generic file access.

---

## 2. `Machine.StateStore.Read@1`

Purpose: return a typed machine-canonical record required by Runtime Host reconciliation/owners.

Allowed record kinds:

```text
INSTALLATION
INSTALLED_BASELINE
OPERATIONAL_MODE
MODE_TRANSITION
UPDATE_TRANSACTION
RECOVERY_CONTEXT
```

Input:

```text
recordKind
recordId
```

Output:

```text
recordKind
recordId
schemaVersion
revision
payload
readUtc
```

Secondary/RDP Runtime Hosts MAY receive only explicitly safe read projections; they do not receive mutation authority.

No arbitrary SQL predicate is accepted.

---

## 3. `Machine.StateStore.Apply@1`

Purpose: atomically persist an owner-approved typed machine-state mutation.

Input:

```text
recordKind
recordId
mutationType
schemaVersion
expectedRevision
operationId
correlationId
idempotencyKey
typed payload
```

Allowed `recordKind × mutationType × schemaVersion` combinations are release-defined.

Broker validates:

```text
caller identity/session
capability authorization
schema compatibility
payload structure
idempotency
expected revision
```

Then executes a bounded repository operation against `machine.db`.

Prohibited input:

```text
SQL text
table name
column name
file path
database path
raw pragma
arbitrary JSON document without record schema
```

Result:

```text
COMMITTED + newRevision
REVISION_CONFLICT
VALIDATION_FAILED
SCHEMA_INCOMPATIBLE
PERSISTENCE_* failure
```

Persistence commit is not automatically a higher-level Work/Game/Update/Recovery semantic success.

---

## 4. `Machine.StateStore.Migrate@1`

Purpose: execute a trusted release-owned machine DB schema migration.

Input:

```text
migrationId
sourceSchemaVersion
targetSchemaVersion
expectedReleaseId
operationId
idempotencyKey
```

`migrationId` resolves only to a migration bundled/authorized by the installed SplitOS release.

Runtime Host cannot submit SQL migration content.

Required behavior:

```text
validate migration/release
→ create SQLite-consistent protected backup
→ validate backup
→ execute migration
→ update schema version metadata
→ quick_check / foreign_key_check
→ report committed migration
```

---

## 5. Authorization matrix extension

| Capability | Active physical-console Runtime Host | Secondary/RDP Host | Manager/Launcher | Idempotency |
|---|---:|---:|---:|---:|
| `Machine.StateStore.Read@1` | yes | limited safe reads only | no | no |
| `Machine.StateStore.Apply@1` | yes when semantic capability permits | no | no | yes |
| `Machine.StateStore.Migrate@1` | maintenance/release-authorized path | no | no | yes |

Machine-owned resume/recovery without an interactive session remains SPEC-11 territory and must use an explicit maintenance/recovery authorization path.

---

## 6. Broker responsibility boundary

Broker owns:

```text
trusted persistence execution
file ACL boundary
schema structural validation
transaction commit/result
```

Broker does not own:

```text
which mode should be committed
whether update is semantically verified
which recovery strategy is correct
what Game Profile user wants
```

This preserves the A&D ownership model while preventing direct user-process tampering with machine-canonical SQLite state.
