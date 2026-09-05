# SPEC-08 — Game Configuration Adapter Contract

## 1. Purpose

Defines the per-game mechanism used to read, validate, apply and verify supported graphics/gameplay configuration fields.

This adapter is separate from SPEC-07 Game Client Adapter.

```text
Game Client Adapter
→ install / launch / process evidence

Game Configuration Adapter
→ supported game-setting read/apply/verify
```

A game may be launch-supported while its configuration adapter is unavailable.

---

## 2. Capability model

Each adapter declares independently:

```text
CONFIG_DISCOVERY
CONFIG_READ
CONFIG_VALIDATE
CONFIG_WRITE_OFFLINE
CONFIG_WRITE_RUNTIME
CONFIG_READBACK
CONFIG_BACKUP_RESTORE
GAME_VERSION_DETECTION
SCHEMA_VERSION_DETECTION
SETTING_DEPENDENCY_VALIDATION
```

Status values reuse the capability approach:

```text
SUPPORTED_PUBLIC
SUPPORTED_OS_MECHANISM
SUPPORTED_VERSION_GATED
BEST_EFFORT_LOCAL_EVIDENCE
VERSION_SENSITIVE
USER_MEDIATED
OPEN
UNSUPPORTED
```

`CONFIG_WRITE_RUNTIME` is expected to be `UNSUPPORTED` for many v1 games.

---

## 3. Release-owned adapter definition

An adapter is release-owned code/knowledge.

It defines:

```text
adapterId
adapterVersion
gameId
supported game version ranges
supported config schema ranges
allowed config locations
setting mapping catalog
serialization rules
backup strategy
apply safety rules
read-back rules
```

User/profile data MUST NOT provide arbitrary config path/key definitions.

---

## 4. Config location resolution

Adapter may resolve only release-defined location strategies, e.g.:

```text
KNOWN_USER_CONFIG_PATH
KNOWN_INSTALL_RELATIVE_PATH
KNOWN_REGISTRY_VALUE
DOCUMENTED_GAME_API
PACKAGE_LOCAL_STATE
```

The resolved path MUST be validated against expected scope.

Forbidden:

```text
profile says configPath=C:\whatever
→ adapter writes it
```

External game-client metadata may help identify install roots, but the configuration adapter must validate any derived path before use.

---

## 5. Typed setting mapping

Generic SplitOS semantic key:

```text
RAY_TRACING_LEVEL
```

may map for one game to:

```text
config field: RayTracingQuality
values: Off / Medium / High / Psycho
```

and for another game to a different schema.

The mapping is adapter-owned.

Concept:

```text
GameSettingMapping
├── semanticKey
├── physical location/key
├── value mapping
├── supported values
├── default/unmanaged semantics
├── dependency rules
├── requires restart
└── verification rule
```

---

## 6. Read contract

Conceptual operation:

```text
ReadManagedConfiguration(gameId, installEvidence, expectedGameVersion)
```

Returns:

```text
configInstanceId
configSchemaVersion
gameVersionEvidence
managedSettingValues[]
unrecognizedManagedFields[]
readUtc
sourceDigest
capabilityStatus
```

Unknown fields are preserved by writer where format allows.

The adapter MUST NOT normalize the entire file into only SplitOS-managed fields and then destroy unrelated user/game data on write.

---

## 7. Validate contract

```text
ValidateCandidateConfiguration(currentConfig, assignments)
```

Checks:

```text
legal values
dependencies
incompatible combinations
hardware prerequisites
config schema support
write phase safety
```

Result:

```text
VALID
VALID_WITH_NORMALIZATION
INVALID_SETTING
INVALID_COMBINATION
UNSUPPORTED_GAME_VERSION
UNSUPPORTED_CONFIG_SCHEMA
WRITE_NOT_SAFE
```

Normalization must be explicit and explainable.

---

## 8. Offline apply baseline

Default v1 managed write phase:

```text
game not running
+ current install/config evidence valid
+ current config read
+ candidate validated
→ atomic/safe write
→ read-back
→ verify managed fields
```

Normal graphics optimization MUST NOT race a running game unless that game adapter explicitly supports runtime mutation.

---

## 9. Safe file write

For file-backed configuration, adapter SHOULD use:

```text
read current file
→ verify expected source digest/version
→ create bounded backup if policy requires
→ serialize to temporary sibling file
→ flush/close
→ atomic replace where filesystem semantics permit
→ reopen/read-back
→ verify managed fields + preservation constraints
```

The exact Windows file primitive is implementation-specific but must preserve the atomicity objective.

---

## 10. Backup semantics

Backup is not the canonical profile.

A bounded backup may support recovery from malformed write.

Backup metadata:

```text
configInstanceId
sourceDigest
createdUtc
adapterVersion
gameVersionEvidence
```

Retention MUST be bounded.

Backup files containing user configuration must inherit user-only ACL expectations.

---

## 11. Registry/package-backed configuration

If game configuration is registry/package-state based:

- adapter must use a typed release-owned key map;
- arbitrary registry paths are forbidden;
- package identity must be validated;
- write capability must be independently tested for current game build;
- read-back is mandatory for AUTO application.

---

## 12. Runtime write capability

`CONFIG_WRITE_RUNTIME` is opt-in per game/setting.

Adapter definition must state:

```text
which settings
which game states
whether UI/game restart is required
how result is verified
rollback/compensation behavior
```

Generic runtime memory modification, DLL injection or process tampering is prohibited.

---

## 13. Apply request

Conceptual request:

```text
ApplyGameConfiguration
├── gameId
├── profileId
├── profileRevision
├── recommendationId
├── configInstanceId
├── expectedSourceDigest
├── expectedGameVersion
├── assignments[]
├── operationId
└── correlationId
```

A stale `expectedSourceDigest` returns conflict rather than overwriting newer external changes.

---

## 14. Apply result

```text
APPLIED_VERIFIED
NO_CHANGE
SOURCE_CHANGED
GAME_RUNNING_WRITE_FORBIDDEN
UNSUPPORTED_VERSION
SCHEMA_CHANGED
VALIDATION_FAILED
WRITE_FAILED
READBACK_FAILED
VERIFY_MISMATCH
BACKUP_FAILED_BLOCKING
```

`APPLIED_VERIFIED` means managed config fields read back as expected.

It still does not guarantee target FPS.

---

## 15. External changes / source digest

The adapter produces a deterministic digest over the relevant config source or managed subset as appropriate.

Before write:

```text
expected digest
vs
current digest
```

Mismatch:

```text
SOURCE_CHANGED
```

This prevents a recommendation calculated from old config from silently overwriting settings changed by the user/game after calculation.

---

## 16. Unknown/new game versions

When game update invalidates adapter support:

```text
CONFIG_READ may remain supported
CONFIG_WRITE may downgrade to UNSUPPORTED/VERSION_SENSITIVE
```

SplitOS prefers read-only/suggest-only degradation over blind writes.

Example:

```text
known config schema v7
new game introduces schema v8
→ no AUTO write until compatibility verified
```

---

## 17. Preservation rule

Adapter write MUST preserve unrelated configuration whenever technically possible.

A SplitOS optimization operation is not permission to reset:

```text
controls
accessibility
language
subtitle preferences
save slots
account state
mods unrelated to managed field set
```

If format cannot be safely edited without broad destructive rewrite, `CONFIG_WRITE` must remain unsupported/user-mediated.

---

## 18. Modded games

Adapters MAY detect known modded/config-extended state.

Default behavior when managed-setting interpretation becomes ambiguous:

```text
AUTO → SUGGEST_ONLY or USER_MANAGED
```

SplitOS MUST NOT delete mods or rewrite unknown custom fields merely to regain support.

---

## 19. Game safe-mode/self-repair changes

Some games rewrite graphics settings after crash/hardware change.

Such changes appear as external drift.

They MUST NOT automatically become permanent SplitOS user locks.

Reconciliation policy from `Overrides Drift and Reconciliation.md` applies.

---

## 20. Configuration provenance

Every read/apply result records:

```text
adapterId/version
game version evidence
config schema version
source location class
source digest
read/apply timestamps
```

Diagnostics can therefore explain exactly which parser/writer produced an outcome.

---

## 21. Security

Adapter runs unelevated wherever normal user game config permits.

It MUST NOT request Broker generic file/registry mutation as a shortcut.

If a supported title truly requires protected configuration mutation, a dedicated bounded capability requires separate security review/spec extension.

No game configuration operation may:

```text
inject into game
patch executable binaries
bypass DRM
bypass anti-cheat
modify protected memory
write arbitrary user-supplied paths
```

---

## 22. Acceptance criteria

- client adapter and config adapter can have different support states;
- AUTO write only occurs against supported game/config versions;
- stale source digest prevents overwrite;
- unknown config fields survive supported write;
- unrelated controls/accessibility settings are preserved;
- game-running write is denied unless explicit capability exists;
- read-back mismatch prevents `APPLIED_VERIFIED`;
- version update can degrade write to SUGGEST_ONLY without breaking launch;
- adapter never accepts arbitrary config path/key definitions from profile/UI.
