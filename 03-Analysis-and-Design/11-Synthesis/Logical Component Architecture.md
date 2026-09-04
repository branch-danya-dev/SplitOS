# SplitOS — Logical Component Architecture

## 1. Purpose

Документ фиксирует логические компоненты конечной архитектуры SplitOS и связывает их с уже установленными responsibilities/owners.

Логический компонент здесь не обязательно равен отдельному `.exe`, service, assembly или deployment unit.

```text
Logical component
!= physical process by definition
```

Physical placement уточняется отдельно.

---

## 2. Component groups

```text
Build Plane
Runtime UX Plane
Runtime Orchestration Plane
Platform Adapter Plane
Privileged Mutation Plane
Persistence Plane
Backend Plane
External Authority Plane
Release Trust Plane
```

---

## 3. Build Plane

### COMP-BLD-01 — Media Builder

Owns orchestration of build-time preparation.

Consumes:

- Windows Source Validator;
- Build Manifest;
- Component Classification knowledge;
- SplitOS packages;
- supported servicing adapters.

Produces:

- prepared baseline;
- build result/evidence;
- baseline identity material.

### COMP-BLD-02 — Windows Source Validator

Validates that supplied Windows source matches supported provenance/version/edition requirements.

Source authority remains Microsoft/external; validator owns SplitOS acceptance decision.

### COMP-BLD-03 — Build Manifest Executor

Executes only typed/versioned actions permitted by the build contract.

No generic arbitrary script execution is part of the canonical product contract.

### COMP-BLD-04 — Baseline Verifier

Reads resulting component state and determines whether required postconditions are satisfied.

```text
build operation success
!= verified supported baseline
```

---

## 4. Runtime UX Plane

### COMP-UX-01 — SplitOS Manager

User-facing control center.

Consumes account, entitlement, settings, profile, update/recovery and diagnostics contracts.

It may request changes but does not directly mutate canonical state.

### COMP-UX-02 — First Run Experience

May be implemented as Manager mode/surface, but remains logically distinct because it owns onboarding UX composition:

```text
Windows user context
→ account sign-in/create
→ association
→ entitlement
→ FREE/PRO branch
```

### COMP-UX-03 — Game Launcher

Game Mode UX and game-session interaction surface.

Consumes:

- unified library;
- Game Profiles;
- game-session state;
- device/runtime status;
- launch orchestration;
- Shared Apps presentation.

---

## 5. Runtime Orchestration Plane

### COMP-RT-01 — Runtime Access Coordinator

Logical owner-facing module for:

- SplitOS account association resolution;
- entitlement evaluation;
- effective ManagedRuntime access;
- FREE/PRO branching.

Must preserve:

```text
Windows authentication
!= SplitOS authentication
```

### COMP-RT-02 — Operational Mode Coordinator

Maintains access to canonical committed operational mode and handles semantic mode requests.

Does not itself own policy application details.

### COMP-RT-03 — Mode Transition Coordinator

Owns transition transaction lifecycle:

```text
REQUESTED
→ INSPECTING
→ RESOLVING
→ APPLYING
→ VERIFYING
→ COMMITTING
```

plus cancel/rollback/fallback paths.

### COMP-RT-04 — Mode Policy Resolver

Resolves semantic target configuration for WORK or GAME.

Consumes application classification, device/profile/user configuration and compatibility policy.

### COMP-RT-05 — Application Lifecycle Coordinator

Classifies and coordinates application/process behavior for transitions.

It must distinguish:

```text
WORK
GAME
GAME_CLIENT
SHARED
SYSTEM
```

### COMP-RT-06 — System Context Coordinator

Logical facade over desired/actual context responsibilities:

- Display Context;
- Audio Context;
- Input Context;
- Power Context.

Internally these remain separate semantic owners and should not collapse into one mutable settings bag.

### COMP-RT-07 — Hardware Context Evaluator

Builds interpreted HardwareSnapshot from Windows/driver/device evidence.

Owns freshness/interpretation of the SplitOS snapshot, not physical device truth.

### COMP-RT-08 — Game Library Projection

Maintains SplitOS unified representation of games and clients.

External client remains authoritative for installation/license/auth evidence.

### COMP-RT-09 — Game Profile Resolver

Owns Game Profile meaning, selection and effective profile composition.

### COMP-RT-10 — Optimization Policy Engine

Produces optimization recommendations/effective optimization intent based on profile, hardware and compatibility knowledge.

It does not own actual GPU/display/device state.

### COMP-RT-11 — Game Launch Orchestrator

Coordinates managed launch from resolved launch intent through external client handoff and actual running evidence.

```text
HANDOFF_ACCEPTED
!= GAME_RUNNING
```

### COMP-RT-12 — Game Session Coordinator

Owns canonical game-session lifecycle independent of operational mode.

### COMP-RT-13 — Shared App Coordinator

Owns semantic representation/presentation policy of allowed Shared Apps in Game Mode.

### COMP-RT-14 — Compatibility Evaluator

Owns supported/unsupported compatibility decisions for release/client/system scenarios.

### COMP-RT-15 — Update Orchestrator

Owns update transaction semantics, not raw servicing commands.

### COMP-RT-16 — Recovery Coordinator

Owns recovery target selection/result semantics.

### COMP-RT-17 — Mutation Coordinator

Logical coordination boundary ensuring conflicting machine mutations are not independently interleaved.

Canonical conflicting families:

```text
Mode Transition
Update
Recovery
```

Exact lock/lease/transaction implementation is OPEN.

### COMP-RT-18 — Observability & Diagnostic Coordinator

Correlates semantic operation IDs/results/events.

Diagnostics remain evidence and must not become canonical truth.

---

## 6. Platform Adapter Plane

### COMP-ADP-01 — Windows Session Adapter

WTS/Win32 session/user evidence.

### COMP-ADP-02 — Process/Application Adapter

Process/window/application runtime evidence.

### COMP-ADP-03 — Display Adapter

Current candidate family:

```text
QueryDisplayConfig
DisplayConfigGetDeviceInfo
SetDisplayConfig
```

with mandatory read-back verification.

### COMP-ADP-04 — Audio Adapter

Discovery/events via Core Audio/MMDevice.

System-wide default endpoint mutation remains OPEN until supported mechanism is validated.

### COMP-ADP-05 — Input Adapter

Device/controller/input evidence and supported configuration operations.

### COMP-ADP-06 — Power Adapter

Power scheme evidence/apply via supported PowrProf APIs where applicable.

### COMP-ADP-07 — Service/System Adapter

Bounded service/platform operations, often delegated through Privileged Broker if elevation is required.

### COMP-ADP-08 — Game Client Adapter Contract

Stable semantic contract implemented by client-specific adapters.

### COMP-ADP-09 — Steam Adapter

Capability-specific Steam implementation. Version-sensitive metadata remains best-effort unless validated.

### COMP-ADP-10 — Epic Adapter

Exact capabilities remain partially OPEN.

### COMP-ADP-11 — Xbox Adapter

Exact capabilities remain partially OPEN.

### COMP-ADP-12 — Battle.net Adapter

Exact capabilities remain partially OPEN.

---

## 7. Privileged Mutation Plane

### COMP-PRIV-01 — Privileged Broker

Runs outside normal interactive privilege and exposes only bounded capabilities.

Trust gates:

```text
IPC connection
→ ACL / caller identity
→ session/context validation
→ request schema validation
→ capability authorization
→ bounded operation
```

### COMP-PRIV-02 — Privileged Capability Handlers

Logical allowlisted handlers, for example:

```text
MachinePolicyHandler
ServicePolicyHandler
UpdateApplyHandler
RecoveryApplyHandler
```

They return technical operation results/evidence; semantic success remains with the calling owner/orchestrator.

---

## 8. Persistence Plane

### COMP-DATA-01 — Local Canonical State Repository

Persists SplitOS-owned durable runtime state that must survive process restart/reboot.

### COMP-DATA-02 — User Configuration Repository

Persists user preferences, Game Profiles and mode/application configuration.

### COMP-DATA-03 — External Projection Cache

Caches external Game Client/device/account evidence with source/freshness metadata.

It is never authoritative for the external source itself.

### COMP-DATA-04 — Transaction Journal

Durable records for transition/update/recovery flows where crash-safe resumption/reconciliation is required.

### COMP-DATA-05 — Diagnostic Store

Bounded logs/events/operation correlation according to future retention/privacy policy.

### COMP-DATA-06 — Protected Secret Store

Logical secure storage for reusable credentials/tokens.

Current Windows-native candidate: user-scoped DPAPI protection.

---

## 9. Backend Plane

### COMP-BE-01 — Account Service

Canonical SplitOS Account identity service.

### COMP-BE-02 — Authentication / Token Service

Supports native-app authentication flow and token lifecycle.

External browser + authorization code + PKCE is current candidate pattern.

### COMP-BE-03 — Entitlement Service

Canonical source of FREE/PRO capability entitlement.

### COMP-BE-04 — Subscription / Payment Reconciliation

Consumes authenticated Payment Provider evidence and updates entitlement according to product rules.

### COMP-BE-05 — Offline Entitlement Issuer

Optional logical component if bounded offline PRO assertions are adopted.

Exact assertion format/TTL/device binding remains OPEN.

### COMP-BE-06 — Release Metadata Service

Potential server-side delivery surface for approved release/update metadata.

Exact ownership/implementation remains Specification-level, but metadata must preserve Release Authority signatures/provenance.

---

## 10. Release Trust Plane

### COMP-REL-01 — Release Signing Authority

Authorizes signed release/build/update metadata.

### COMP-REL-02 — Artifact Registry / Repository

Stores/distributes exact release artifacts and metadata.

Transport alone does not make artifacts trusted.

### COMP-REL-03 — Key Lifecycle Authority

Owns rotation/revocation/emergency trust policy.

Exact key hierarchy remains OPEN.

---

## 11. External Authority Plane

External authorities remain outside SplitOS ownership:

```text
Microsoft Windows source/servicing ecosystem
Windows platform / drivers / devices
Steam / Epic / Xbox / Battle.net
Payment Provider
Microsoft/Local Windows account authority
```

SplitOS consumes claims/evidence through bounded contracts.

---

## 12. Component interaction rules

### Rule 1 — UX never writes canonical state directly

```text
Manager/Game Launcher
→ semantic request
→ owner/orchestrator
```

### Rule 2 — Adapter evidence is not owner truth

```text
Adapter evidence
→ semantic owner interpretation
→ canonical update if justified
```

### Rule 3 — Privilege is an execution boundary

```text
Privileged Broker
!= semantic decision owner
```

### Rule 4 — Persistence does not create ownership

A repository stores owner-approved state; storage code does not independently decide mode, entitlement or compatibility.

### Rule 5 — Backend authority is scoped

Account/entitlement backend owns product identity/access facts, but does not own Windows session state or local operational mode.

### Rule 6 — one owner per canonical fact

No component may maintain a competing independently mutable copy of canonical truth.

---

## 13. Candidate physical grouping

A reasonable v1 physical grouping, without making it mandatory yet:

```text
SplitOS.Manager.exe
  └── Manager + First Run UI

SplitOS.GameLauncher.exe
  └── Game Mode UX

SplitOS.RuntimeHost.exe
  ├── runtime orchestration modules
  ├── Windows adapters usable in user session
  ├── Game Client adapters
  └── local persistence clients

SplitOS.Broker.Service.exe
  └── Privileged Broker + capability handlers

SplitOS.MediaBuilder.exe
  └── build plane

SplitOS Backend
  └── account/auth/entitlement/subscription services
```

This grouping is a synthesis candidate, not a requirement that every logical component be a separate binary.

---

## 14. Result

The component model preserves the methodology chain:

```text
Responsibility
→ Owner
→ Logical Component
→ Contract
→ Integration
→ Runtime/Build placement
```

without collapsing semantic ownership into UI, storage, adapter or privileged execution concerns.