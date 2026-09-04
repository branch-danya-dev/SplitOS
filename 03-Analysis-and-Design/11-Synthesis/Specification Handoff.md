# SplitOS — Specification Handoff

## 1. Purpose

Этот документ завершает Analysis & Design и определяет, какие detailed specifications должны следовать дальше.

Synthesis достаточно стабилен, чтобы перейти к:

```text
Detailed Specification
→ Grooming / implementation planning
→ Delivery support
→ Verification
```

Но оставшиеся OPEN должны закрываться отдельными решениями/исследованиями, а не неявными assumption в коде.

---

## 2. Recommended specification packages

### SPEC-01 — Runtime Process & Module Specification

Должен определить:

- physical packaging Manager / Game Launcher / Runtime Host;
- internal Runtime Host module boundaries;
- process startup/lifecycle/restart rules;
- user/session cardinality;
- dependency injection/module contracts if relevant;
- operation correlation IDs;
- version compatibility between local components.

Must preserve logical responsibilities from `Logical Component Architecture.md`.

---

### SPEC-02 — Local IPC & Privileged Broker Specification

Должен определить:

- Named Pipe or alternative final transport;
- protocol versioning;
- request/response envelope;
- caller SID/session/process validation;
- explicit SDDL/ACL;
- broker service identity;
- capability authorization matrix;
- timeout/cancellation/idempotency semantics;
- allowlisted privileged operations;
- audit requirements;
- service hardening.

Must explicitly prohibit arbitrary command execution surface.

---

### SPEC-03 — Local Data & Persistence Specification

Должен определить:

- physical storage engine(s);
- user vs machine storage separation;
- schema for canonical local state;
- Game Profile schema;
- transaction journal schema;
- projection cache schema/source/freshness metadata;
- migration/versioning rules;
- corruption handling;
- atomic/durable write requirements;
- secret-store integration;
- diagnostic retention.

---

### SPEC-04 — Account/Auth/Entitlement Specification

Должен определить:

- account backend APIs/contracts;
- OAuth/OIDC provider;
- native application authorization flow;
- redirect strategy;
- PKCE details;
- token types/lifetimes/refresh/revocation;
- protected token storage;
- Windows user association semantics;
- FREE/PRO entitlement model;
- offline entitlement assertion format/TTL/context binding;
- clock rollback / stale entitlement behavior;
- device/account cardinality.

---

### SPEC-05 — Mode Runtime Specification

Должен определить:

- OperationalMode persistence semantics;
- exact ModeTransition transaction schema;
- blocker detection plugins/rules;
- Work/Game policy representation;
- application lifecycle rules;
- transition ordering;
- verification requirements;
- cancellation/rollback behavior;
- major mutation coordination primitive;
- startup/reboot reconciliation.

---

### SPEC-06 — Windows Context Integration Specification

Separate sections for:

```text
Display
Audio
Input
Power
Process/Application
Services/System
Hardware discovery
```

Must define:

- concrete Windows APIs;
- supported Windows versions;
- desired vs actual state representation;
- read-back verification;
- device identity/freshness rules;
- permission requirements;
- unsupported/fallback behavior.

Priority engineering OPEN:

```text
supported system-wide default audio switching mechanism
```

---

### SPEC-07 — Game Client Adapter Specification

One shared semantic adapter contract plus client-specific capabilities.

Must define for each supported client:

- client detection;
- library/install evidence;
- external game IDs;
- launch handoff;
- auth-required outcome;
- process/game correlation;
- start/exit evidence;
- refresh/invalidation;
- version-sensitive mechanisms;
- capability status.

Initial engineering research still required for Epic/Xbox/Battle.net and stable Steam metadata boundaries.

---

### SPEC-08 — Game Profile & Optimization Specification

Must define:

- Game Profile schema;
- profile selection rules;
- display/input association;
- hardware snapshot validity;
- default/recommended/user override precedence;
- optimization constraints;
- supported game configuration mechanisms;
- incompatibility/fallback handling;
- user override persistence.

Must preserve anti-cheat/DRM/network/input-cheating boundaries.

---

### SPEC-09 — Game Launcher & Shared Apps UX Specification

Must define:

- navigation/controller model;
- library states;
- Game Session presentation;
- error/auth/update states;
- Game Profile selection/edit surface;
- Shared App presentation modes;
- max-active Shared Apps v1 policy;
- Switch to Work behavior;
- accessibility/performance requirements.

---

### SPEC-10 — Builder & Component Matrix Specification

Must define:

- supported Windows source acquisition/input policy;
- source provenance validation;
- supported editions/builds;
- Build Manifest schema;
- typed action set;
- component inventory;
- `REMOVE / DISABLE / MODE_MANAGED / KEEP` decisions;
- dependencies;
- servicing/reboot implications;
- post-build verification;
- reproducibility requirements;
- failed-build semantics.

Windows Component Matrix requires empirical testing and must not be completed only from assumptions.

---

### SPEC-11 — Update & Recovery Specification

Must define:

- update metadata/package format;
- compatibility gates;
- release transition policy;
- signed manifest verification;
- protected staging;
- UpdateTransaction durability;
- reboot/resume protocol;
- actual-state verification;
- rollback/snapshot/reinstall strategy;
- recovery targets;
- manual recovery path;
- anti-downgrade policy;
- last-known-good semantics.

Exact update/rollback technology remains a major engineering decision.

---

### SPEC-12 — Release Security & Key Management Specification

Must define:

- signing key hierarchy;
- online/offline key separation;
- manifest signature envelope;
- artifact digest binding;
- Authenticode strategy;
- key rotation;
- key revocation;
- compromised-key response;
- release metadata versioning;
- downgrade/recovery authorization;
- CI/release approval chain.

---

### SPEC-13 — Observability & Diagnostics Specification

Must define:

- semantic event catalog;
- operation IDs;
- transition/update/recovery/game-session correlation;
- privacy-sensitive fields;
- secret redaction;
- local retention;
- user export/support bundle;
- remote telemetry policy if any;
- FREE/PRO differences if any.

Diagnostics must remain evidence, not canonical control state.

---

### SPEC-14 — Verification & Acceptance Specification

Should translate requirements/architecture into testable cases.

Minimum families:

```text
Build verification
FREE runtime access
PRO entitlement activation
Work→Game
Game launch
Game exit
Game→Work
Device loss/fallback
Runtime Host/Broker crash
Offline entitlement
Update/reboot/resume
Recovery
Trust boundary abuse tests
Artifact tampering/signature tests
```

---

## 3. Engineering research backlog before/during specification

High-priority validation tracks:

### ENG-01 — Windows component removal matrix

Empirically test dependencies/servicing after REMOVE/DISABLE decisions.

### ENG-02 — Default audio endpoint switching

Validate a supported, maintainable mechanism or redesign product semantics if unavailable.

### ENG-03 — Game client adapters

Validate real supported mechanisms and failure behavior for target client versions.

### ENG-04 — Update/rollback technology

Prototype transaction, reboot continuation and recovery before locking detailed spec.

### ENG-05 — Windows source acquisition/provenance

Resolve legal/licensing/technical supported model.

### ENG-06 — Privileged Broker threat/hardening prototype

Validate service account, pipe ACL, caller checks and capability model.

### ENG-07 — Offline entitlement abuse model

Evaluate TTL, device binding, clock tampering and offline UX trade-offs.

### ENG-08 — Performance baseline

Measure Runtime Host/Game Launcher overhead, transition latency and gaming impact.

---

## 4. Suggested implementation slices

A possible incremental delivery strategy:

```text
Slice 0 — Baseline skeleton
Manager + Runtime Host + local state + Broker secure hello

Slice 1 — FREE product identity
First Run + Account + FREE Windows experience

Slice 2 — Manual Work/Game mode foundation
Mode state + transition + display/power/basic application policy

Slice 3 — Game Launcher + Steam MVP
Library projection + profiles + managed launch + exit

Slice 4 — Recovery/transaction hardening
crash/reboot reconciliation + rollback

Slice 5 — Builder baseline
source validation + manifest executor + component matrix subset

Slice 6 — Update system
signed update + durable transaction + recovery

Slice 7 — additional clients/device integrations
Epic/Xbox/Battle.net/audio/etc
```

This is a planning suggestion, not a replacement for Product/Grooming prioritization.

---

## 5. Definition of A&D ready for Specification

A&D baseline is ready when:

- system boundary is explicit;
- responsibilities/owners are unique;
- state semantics are explicit;
- canonical flows/failures/trust boundaries are explicit;
- logical components preserve owners;
- deployment/process boundaries are explicit;
- canonical/open mechanism distinction is preserved;
- remaining engineering unknowns are explicit backlog items.

Current Synthesis satisfies these conditions at architecture level.

---

## 6. Change discipline after Synthesis

If detailed specification discovers conflicting evidence, do not silently patch implementation.

Use:

```text
New evidence
→ update Discovery/Decision status
→ assess affected Requirement
→ update owning A&D layer
→ update Synthesis
→ update Specification
```

This preserves the SSAD traceability chain instead of letting implementation become a hidden source of truth.

---

## 7. Result

The next phase is no longer “продолжать придумывать архитектуру”.

It is:

```text
select one specification package
→ close its explicit OPEN decisions with evidence
→ define implementable contracts/data/algorithms
→ groom into delivery slices
→ verify against canonical architecture
```

Synthesis therefore marks the end of the current Analysis & Design baseline, not the end of analysis throughout the product lifecycle.