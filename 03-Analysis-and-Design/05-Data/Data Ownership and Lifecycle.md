# SplitOS — Data Ownership and Lifecycle

## 1. Purpose

Документ переводит Ownership Model в data semantics.

Для каждого значимого вида данных необходимо определить:

```text
Meaning
Canonical owner
Source/evidence
Allowed writers
Consumers
Persistence need
Freshness/lifecycle
Conflict rule
```

Это всё ещё conceptual/logical analysis, а не physical storage design.

---

# 2. Persistence classes

Используются следующие смысловые классы persistence need.

## REQUIRED_CANONICAL

Данные должны переживать restart/reboot настолько, насколько это необходимо для сохранения product truth.

Examples:

```text
GameProfile
Entitlement cache/state according to offline policy
InstalledBaselineIdentity
Build/Release identity
last committed OperationalModeState
transition recovery metadata where required
```

---

## VERSIONED_PRODUCT_KNOWLEDGE

Версионированное product/release knowledge.

Examples:

```text
BuildManifest
ComponentClassificationDecision
CompatibilityDecision
supported game configuration knowledge
```

---

## PROJECTION_CACHE

Локальная проекция внешнего authority.

Examples:

```text
GameInstallationProjection
external client library snapshot
license/account availability projection
```

Must be reconcilable/refetchable.

---

## EPHEMERAL_EVIDENCE

Временные runtime observations.

Examples:

```text
current process list
actual service state
current device presence
```

May be retained diagnostically, but is not long-term canonical truth.

---

## TRANSACTION_RECOVERY

Данные хранятся ради завершения/rollback/recovery operation.

Examples:

```text
ModeTransitionRecord
UpdateTransactionRecord
RecoveryContext
```

Retention after terminal outcome is policy-dependent.

---

## DIAGNOSTIC

Логи и telemetry/diagnostic events SplitOS.

Never become canonical truth by themselves.

---

# 3. Core ownership matrix

| Data concept | Canonical owner | Source / evidence | Persistence class | Conflict rule |
|---|---|---|---|---|
| SplitOSAccount | Product Identity & Entitlement | auth/account evidence | REQUIRED_CANONICAL / remote-authoritative TBD | external auth evidence maps into one SplitOS identity |
| Entitlement | Product Identity & Entitlement | subscription/license/payment evidence | REQUIRED_CANONICAL with offline semantics TBD | payment evidence does not directly equal entitlement |
| SplitOSRelease | Distribution / Compatibility knowledge | release definition | VERSIONED_PRODUCT_KNOWLEDGE | release version is immutable identity once published conceptually |
| WindowsBase support | Compatibility Management | Windows metadata + tests | VERSIONED_PRODUCT_KNOWLEDGE | test evidence changes decision through explicit compatibility update |
| BuildManifest | Distribution Engineering | release definition | VERSIONED_PRODUCT_KNOWLEDGE | Builder consumes; Builder cannot redefine manifest |
| ComponentClassificationDecision | Distribution Engineering | dependency/tests | VERSIONED_PRODUCT_KNOWLEDGE | one classification per component/release context |
| InstalledBaselineIdentity | Installed Runtime / release context | successful build/install/update | REQUIRED_CANONICAL | observed drift does not rewrite expected identity |
| BaselineDriftObservation | Compatibility/runtime validation | actual component evidence | EPHEMERAL_EVIDENCE / diagnostic retention | observation cannot redefine expected baseline |
| OperationalModeState | Mode Intent & Active Mode State | committed transition result | REQUIRED_CANONICAL | UI/process evidence cannot override canonical committed mode |
| ModeTransitionRecord | Mode Transition Coordination | transition actions/results | TRANSACTION_RECOVERY | one active transition at a time |
| ApplicationClassification | Application Lifecycle Policy | product metadata/user config where allowed | REQUIRED_CANONICAL / policy | external executable identity does not define SplitOS class automatically |
| ApplicationLifecyclePolicy | Application Lifecycle Policy | mode policy/config | REQUIRED_CANONICAL / versioned config | runtime executor cannot invent policy |
| SharedAppProfile | Shared App Experience | user/config policy | REQUIRED_CANONICAL | application internal state remains external |
| GameClient | Game Library Representation | supported integration knowledge | VERSIONED_PRODUCT_KNOWLEDGE + runtime evidence | external client remains platform authority |
| Game | Game Library Representation | reconciled supported client metadata | REQUIRED_CANONICAL product identity | executable path alone is insufficient identity |
| GameInstallationProjection | Game Library Representation | external Game Client | PROJECTION_CACHE | client authority wins on disagreement |
| GameProfile | Game Profiles | user choices + SplitOS recommendations | REQUIRED_CANONICAL | only Game Profiles ownership writes canonical profile |
| HardwareSnapshot | Hardware Context Evaluation | Windows/driver/device | EPHEMERAL_EVIDENCE / cached snapshot | newer valid snapshot supersedes old evidence, not user preferences |
| Display/Input/Audio preference | corresponding Context owner / Game Profiles | user/mode/profile intent | REQUIRED_CANONICAL | actual device state does not erase preference |
| CompatibilityDecision | Compatibility Management | tests + external version evidence | VERSIONED_PRODUCT_KNOWLEDGE | consumers cannot independently mark supported |
| UpdateTransactionRecord | Update Orchestration | update operations | TRANSACTION_RECOVERY | entitlement/compatibility remain referenced separate facts |
| RecoveryContext | Recovery Coordination | state/transaction/failure evidence | TRANSACTION_RECOVERY | recovery consumes underlying owners, does not copy truth permanently |
| DiagnosticRecord | Observability & Diagnostics | events from owners | DIAGNOSTIC | log mismatch triggers investigation, not automatic truth replacement |

---

# 4. Identity rules

## 4.1 Internal identifiers vs external identifiers

SplitOS will need internal stable identity for several concepts while preserving external IDs as mappings.

Conceptually:

```text
SplitOS Game ID
        ↕ maps to
Steam App ID / Epic Catalog ID / Xbox identity / etc.
```

Exact identifier format is deferred.

Rule:

> External IDs should not automatically become universal SplitOS primary identity unless analysis proves that identity stable and cross-context sufficient.

---

## 4.2 Game identity ambiguity

Open case:

```text
same title available through Steam and Epic
```

Possible semantic models:

```text
one Game
+ two GameInstallationProjections
```

or, for edge cases where builds differ materially:

```text
separate playable variants under shared title family
```

No physical decision is made yet.

---

## 4.3 Device identity ambiguity

Display/audio/input identity may change due to:

- reconnect;
- driver reinstall;
- port change;
- EDID/driver behavior;
- device replacement.

Therefore configuration should not assume a raw transient Windows identifier is necessarily permanent product identity.

---

# 5. Lifecycle — Game data

Conceptual lifecycle:

```text
External client discovered
        ↓
Game Client reconciled
        ↓
Game Installation Projection created/updated
        ↓
Game linked/resolved
        ↓
GameProfile created or defaulted
        ↓
Hardware snapshot refreshed
        ↓
Effective launch context resolved
        ↓
Game session
        ↓
profile/history retained according to policy
```

If installation disappears externally:

```text
GameInstallationProjection → unavailable/removed after reconciliation
```

but user GameProfile should not necessarily be deleted.

This allows reinstalling the game later without losing preferences.

---

# 6. Lifecycle — Mode state

Canonical committed mode should survive enough failure scenarios to support deterministic recovery.

Conceptually:

```text
last committed mode
+
active transition metadata
+
commit evidence/outcome
```

must allow reboot logic to distinguish:

```text
WORK committed, GAME transition interrupted
```

from:

```text
GAME successfully committed before reboot
```

Raw running-process evidence is insufficient.

---

# 7. Lifecycle — Release / baseline

```text
SplitOSRelease published
        ↓
Installation built/deployed
        ↓
InstalledBaselineIdentity = Release R
        ↓
Runtime validation
        ↓
possible DriftObservation
        ↓
repair/update/recovery
        ↓
InstalledBaselineIdentity changes only after successful supported lifecycle operation
```

A drift observation must never mutate release definition.

---

# 8. Lifecycle — Entitlement

Conceptual states are not finalized, but data behavior must support:

```text
account known
entitlement known
backend temporarily unavailable
cached/offline decision applied by explicit policy
entitlement changed/expired
reconciliation
```

Important rules:

- entitlement loss does not corrupt installed baseline;
- Windows license state is independent;
- game-platform licenses are independent;
- cached entitlement must have explicit freshness/expiry semantics later.

---

# 9. Freshness rules

Different data types require different freshness semantics.

## Release knowledge

Freshness tied to explicit version/release.

## HardwareSnapshot

Freshness tied to time/event and should be refreshed at required behavior points.

Current requirement examples:

```text
Game Launcher startup
before game launch when meaningful change may exist
```

## GameInstallationProjection

Freshness tied to reconciliation with external client.

## Actual-state evidence

Freshness is short-lived and operation-specific.

---

# 10. Copies and caches

SplitOS may duplicate data physically later for performance/offline behavior, but semantic ownership must remain one.

Rule:

```text
Canonical owner
        ↓
materialized copy/cache
        ↓
consumer
```

Never:

```text
copy A becomes truth
copy B becomes another truth
```

Every cache/projection must conceptually define:

- source authority;
- last reconciliation point;
- stale behavior;
- conflict behavior.

---

# 11. Deletion semantics

Deletion is not uniform across data concepts.

Examples:

## Game uninstall

May remove/mark unavailable:

```text
GameInstallationProjection
```

but should not automatically delete:

```text
GameProfile
```

unless explicit user/product policy requires it.

## Account sign-out

Should not necessarily destroy local installation/baseline identity.

## SplitOS update

Must preserve/migrate supported user profiles according to update requirements.

## Diagnostic retention expiry

May delete old DiagnosticRecords without affecting system truth.

---

# 12. Data migration requirement

When schema/meaning changes between SplitOS releases, migration must preserve semantic ownership.

Conceptual categories:

```text
UNCHANGED
MIGRATABLE
REQUIRES_RECALCULATION
INCOMPATIBLE
DEPRECATED
```

Example:

A GameProfile field based on old optimization knowledge may require recalculation, while explicit user preference should be preserved if still meaningful.

---

# 13. Privacy boundary

Data layer must later distinguish at least:

```text
local-only operational data
account-linked profile data
entitlement/account data
diagnostic/support data
external platform projections
```

No cloud-sync assumption is made here.

Diagnostics must not automatically include arbitrary user/application content merely because SplitOS observes process lifecycle.

Exact privacy/retention specification belongs Trust/Security/Data physical design.

---

# 14. Data invariants

### DATA-INV-001
One canonical owner per significant fact.

### DATA-INV-002
External projection never silently becomes external authority.

### DATA-INV-003
Desired configuration and actual state remain distinct.

### DATA-INV-004
Observed baseline drift does not rewrite expected baseline.

### DATA-INV-005
Game uninstall does not inherently imply GameProfile deletion.

### DATA-INV-006
Hardware snapshot does not overwrite persistent user preference merely because hardware is temporarily unavailable.

### DATA-INV-007
Diagnostic data does not own operational truth.

### DATA-INV-008
A transaction/recovery record must preserve enough semantics to determine whether commit was reached.

---

# 15. Result

The Data layer now distinguishes:

```text
Canonical product truth
Policy / user intent
External projections
Runtime evidence
Transaction/recovery state
Diagnostics
```

This distinction is the foundation for the next Interface layer, because every future contract can now state whether it reads/writes canonical data, requests a decision, or merely supplies evidence.
