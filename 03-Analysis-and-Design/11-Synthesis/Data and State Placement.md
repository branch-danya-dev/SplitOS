# SplitOS — Data and State Placement

## 1. Purpose

Документ связывает conceptual data model с конечной component/deployment architecture.

Он не выбирает конкретную БД или schema. Его цель — определить:

```text
what data exists
→ who owns it
→ where it logically lives
→ required durability
→ who may read/write it
→ what is canonical vs cached/evidence
```

---

## 2. Placement classes

```text
BACKEND_CANONICAL
MACHINE_CANONICAL
USER_CANONICAL
TRANSACTION_DURABLE
PROJECTION_CACHE
EPHEMERAL_EVIDENCE
PROTECTED_SECRET
DIAGNOSTIC
RELEASE_KNOWLEDGE
```

---

## 3. Backend canonical data

### SplitOSAccount

Owner:

```text
Product Identity & Entitlement / Account Backend
```

Placement:

```text
BACKEND_CANONICAL
```

Local machine may persist association/reference information, but not redefine account identity.

### Entitlement

Owner:

```text
Product Identity & Entitlement
```

Placement:

```text
BACKEND_CANONICAL
+
optional bounded offline assertion locally
```

A local cache is evidence, not editable canonical entitlement.

### Payment evidence projection

Authoritative source:

```text
Payment Provider
```

SplitOS Backend stores/reconciles validated evidence for entitlement decision.

Desktop does not own payment truth.

---

## 4. User-scoped canonical local data

### WindowsUserAccountAssociation

Meaning:

```text
current Windows user context
↔ SplitOS Account reference
```

Logical placement:

```text
USER_CANONICAL local association metadata
```

Account itself remains backend-owned.

### GameProfile

Owner:

```text
Game Profiles
```

Placement:

```text
USER_CANONICAL
```

May later sync to backend if product requires it, but local-vs-cloud sync ownership is OPEN.

### User preferences

Examples:

- default display preference;
- input preference;
- Shared App preferences;
- mode UI preferences;
- profile selection preferences.

Placement:

```text
USER_CANONICAL
```

Exact synchronization model remains OPEN.

---

## 5. Machine-scoped canonical local data

### InstalledBaselineIdentity

Owner:

```text
Distribution / Update lifecycle semantics
```

Placement:

```text
MACHINE_CANONICAL
```

Must reflect only a verified installed baseline.

### SplitOSInstallation

Represents the local installation identity/version relationship.

Placement:

```text
MACHINE_CANONICAL
```

### Component/baseline metadata

Expected installed component/version metadata may be persisted machine-wide for verification/recovery.

It does not replace actual Windows state read-back.

---

## 6. Operational runtime state

### OperationalModeState

Owner:

```text
Mode Intent & Active Mode State
```

Placement requirement depends on crash/reboot policy.

At minimum:

```text
runtime canonical state
```

If startup/recovery semantics require surviving Runtime Host crash/reboot, committed state must be durably represented.

Synthesis recommendation:

```text
committed operational mode → durable machine/user-session scoped canonical state
```

Exact persistence format remains Specification-level.

### ManagedRuntimeAccessDecision

Derived from:

```text
account association + entitlement/offline evidence + policy
```

Normally recomputable at session startup.

Placement:

```text
runtime canonical decision
+
limited durable evidence as needed
```

Do not persist it as independent editable truth.

---

## 7. Durable transaction data

### ModeTransitionRecord

Placement:

```text
TRANSACTION_DURABLE
```

when crash-safe reconciliation is required.

Must preserve enough information to answer after restart:

```text
what operation was active?
what was source committed mode?
what was target?
what stage was reached?
was commit durable?
what rollback/recovery evidence exists?
```

### UpdateTransactionRecord

Placement:

```text
TRANSACTION_DURABLE machine-wide
```

Required across reboot/power loss.

### RecoveryContext

Placement:

```text
TRANSACTION_DURABLE machine-wide
```

Must not disappear merely because Runtime Host restarted.

---

## 8. External projection caches

### GameInstallationProjection

Authoritative source:

```text
External Game Client / Platform
```

Placement:

```text
PROJECTION_CACHE
```

Must carry:

- source client;
- external game identifier;
- observation timestamp/freshness;
- confidence/capability status where relevant.

### Unified Game Library projection

SplitOS owns the unified representation, but external install/license facts remain bounded evidence.

Local projection can be cached for UX/performance but must be reconciled.

### External client availability/auth state

Placement:

```text
PROJECTION_CACHE or EPHEMERAL_EVIDENCE
```

depending on semantic usefulness/freshness.

---

## 9. Hardware/device evidence

### HardwareSnapshot

Owner of interpreted snapshot:

```text
Hardware Context Evaluation
```

Underlying authority:

```text
Windows / driver / device
```

Placement:

```text
EPHEMERAL_EVIDENCE
+
optional short-lived cache
```

Must carry freshness/version/invalidation semantics.

### Display/Audio/Input endpoint representations

Primarily runtime evidence/projections.

Stable user preferences should reference a logical/stable device identity strategy, but actual enumeration remains current evidence.

Exact stable device-ID mapping is OPEN.

---

## 10. Policy/configuration data

### ModePolicy

Placement:

```text
RELEASE_KNOWLEDGE + local configuration composition
```

Product defaults/versioned rules should be tied to supported release knowledge.

User overrides/preferences remain user-scoped.

### ApplicationClassification / LifecyclePolicy

Can consist of:

```text
release-defined defaults
+
compatibility knowledge
+
user-approved configuration where allowed
```

No single writable `settings.json` should silently become owner of all policy.

### CompatibilityDecision

Placement:

```text
RELEASE_KNOWLEDGE / versioned product knowledge
```

May be delivered/cached locally, but must retain provenance/version scope.

---

## 11. Build/release knowledge

### BuildManifest

Placement:

```text
RELEASE_KNOWLEDGE
```

Must be versioned/signed according to Trust layer.

### ComponentClassificationDecision

Placement:

```text
RELEASE_KNOWLEDGE
```

Classification changes may require new clean baseline or supported update path depending lifecycle policy.

### Release metadata

Must bind:

```text
release identity
→ required component set
→ versions/digests
→ compatibility constraints
→ allowed transitions
```

---

## 12. Protected secrets

Examples:

- reusable account refresh/access credentials where necessary;
- PKCE/native auth transient material if persisted at all;
- local protected recovery/session secrets if introduced.

Placement:

```text
PROTECTED_SECRET
```

Current candidate:

```text
Windows user-scoped DPAPI
```

Rules:

- never plaintext config;
- never logs;
- never exposed to Game Launcher unnecessarily;
- no backend client secret embedded in public desktop client.

---

## 13. Diagnostics

### DiagnosticRecord

Placement:

```text
DIAGNOSTIC
```

Contains operation correlation/evidence, for example:

```text
transitionId
updateTransactionId
recoveryId
gameSessionId
integration result
verification result
failure classification
```

Diagnostics must not be used as an alternative canonical state repository.

Retention/privacy/upload policy remains Specification-level.

---

## 14. Write authority matrix

| Data | Semantic writer | Other consumers |
|---|---|---|
| SplitOSAccount | Backend Account authority | Runtime/Manager |
| Entitlement | Backend Entitlement authority | Runtime Access, Manager |
| WindowsUserAccountAssociation | Product identity association logic | Runtime/Manager |
| ManagedRuntimeAccessDecision | Product Identity & Entitlement | Runtime/UX |
| OperationalModeState | Mode State owner | Launcher/Manager/orchestrators |
| ModeTransitionRecord | Transition Coordinator | Recovery/diagnostics |
| GameProfile | Game Profiles owner | Launch/Optimization/UX |
| GameInstallationProjection | Game Library Representation from external evidence | Launcher/Launch |
| HardwareSnapshot | Hardware Context Evaluation | Profiles/Optimization/Transition |
| CompatibilityDecision | Compatibility Management | Update/Launch/Builder |
| UpdateTransactionRecord | Update Orchestration | Recovery/diagnostics |
| RecoveryContext | Recovery Coordination | Runtime/diagnostics |
| InstalledBaselineIdentity | verified build/update lifecycle | Runtime/Update/Recovery |
| DiagnosticRecord | Observability | support/analysis only |

Storage infrastructure itself is not the semantic writer.

---

## 15. Persistence invariants

1. Canonical facts have exactly one semantic writer/owner.
2. External projections preserve source and freshness.
3. Transaction records required for reboot/crash recovery are durable before irreversible/ambiguous phases.
4. Diagnostics never replace canonical state.
5. Secret material is isolated from general configuration/logging.
6. Machine-wide state and per-user state remain distinguishable.
7. Cached entitlement is not equivalent to server authority unless it is a bounded verifiable offline assertion.
8. Installed baseline identity changes only after verification.
9. Actual hardware/Windows state must be re-read where semantic correctness depends on freshness.
10. Physical storage technology may change without changing ownership semantics.

---

## 16. Candidate storage decomposition

Without selecting technology, a reasonable physical decomposition is:

```text
Per-user durable store
├── SplitOS account association
├── user preferences
├── Game Profiles
└── user projection metadata

Protected user secret store
└── account credentials/tokens

Machine durable store
├── InstalledBaselineIdentity
├── machine runtime metadata
├── UpdateTransactionRecord
└── RecoveryContext

Runtime transaction/state store
├── committed operational state
└── active/recent ModeTransitionRecord

Projection caches
├── Game Client/library evidence
└── short-lived device/hardware evidence

Diagnostics store
└── correlated events/failures
```

Exact database/files/registry choices remain open.

---

## 17. Result

Data placement preserves the core chain:

```text
Owner
→ Canonical data/evidence class
→ Logical repository
→ Durability
→ Consumers
```

and prevents persistence technology from accidentally becoming the owner of system meaning.