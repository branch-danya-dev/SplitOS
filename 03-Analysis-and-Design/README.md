# SplitOS — Analysis & Design

Этот каталог содержит канонический Analysis & Design baseline SplitOS после Discovery, Concept и Requirements.

## Canonical sequence

```text
00-Boundaries
    ↓
01-Responsibilities
    ↓
02-Ownership
    ↓
03-States
    ↓
04-Behavior
    ↓
05-Data
    ↓
06-Interfaces
    ↓
07-Integrations
    ↓
08-Flows
    ↓
09-Failures
    ↓
10-Trust
    ↓
11-Synthesis
```

Порядок отражает reasoning dependency, а не жёсткий waterfall. Новое VERIFIED evidence может потребовать возврата к предыдущему owner layer.

---

## Current status

| Layer | Status | Canonical artifacts |
|---|---|---|
| `00-Boundaries` | READY | System Boundary, Build Pipeline, Installed Runtime, Component Classification |
| `01-Responsibilities` | READY | Responsibility Model |
| `02-Ownership` | READY | Ownership Model |
| `03-States` | READY | System, Runtime Access, Mode Transition, Game Session |
| `04-Behavior` | READY | First Run, Startup, Work→Game, Game Launch, Game→Work |
| `05-Data` | READY | Domain, Identity/Access, Configuration, Ownership/Lifecycle |
| `06-Interfaces` | READY | Interface Model, Internal/External contracts |
| `07-Integrations` | READY | Windows, Game Clients, Account/Payment, Builder/Update mechanisms |
| `08-Flows` | READY | First Run, Work→Game, Launch/Exit, Game→Work, Update/Recovery |
| `09-Failures` | READY | Failure taxonomy, runtime/update/recovery scenarios, handling matrix |
| `10-Trust` | READY | IPC/privilege, identity/entitlement/secrets, artifact/update trust, external evidence |
| `11-Synthesis` | READY | System Synthesis, Logical Components, Deployment, Data Placement, Baseline Matrix, Specification Handoff |

```text
Analysis & Design baseline = COMPLETE
Next phase = Detailed Specification
```

---

# Knowledge ownership by layer

## Boundary truth

```text
00-Boundaries/
```

Defines product/build/runtime/external boundaries.

## Responsibility truth

```text
01-Responsibilities/Responsibility Model.md
```

Defines what the system must own semantically.

## Ownership truth

```text
02-Ownership/Ownership Model.md
```

Defines decision authority, canonical writers, consumers and evidence sources.

## State truth

```text
03-States/
```

Core invariant:

```text
FREE → ManagedRuntime=DISABLED, OperationalMode=NONE
PRO  → ManagedRuntime=ENABLED, WORK xor GAME
```

## Behavior truth

```text
04-Behavior/
```

Defines trigger → rules → state consequences.

## Data truth

```text
05-Data/
```

Separates canonical product truth, user policy/intent, external projections, runtime evidence, transaction/recovery state and diagnostics.

## Interface truth

```text
06-Interfaces/
```

Defines semantic provider/consumer contracts without assuming transport.

## Integration truth

```text
07-Integrations/
```

Mechanisms remain explicitly classified as `VERIFIED / CANDIDATE / BEST_EFFORT / OPEN / REJECTED`.

## Flow truth

```text
08-Flows/
```

Canonical temporal pattern:

```text
Trigger
→ Preconditions
→ Request/Evidence
→ Owner decision
→ Integration operation
→ Actual-state evidence
→ Verification
→ Commit/result
→ User-visible outcome
```

## Failure truth

```text
09-Failures/
```

Failure handlers never invent canonical state; safe convergence must be verified.

## Trust truth

```text
10-Trust/
```

Core trust chain:

```text
Claim/Request
→ Identity/Issuer
→ Integrity
→ Freshness
→ Context binding
→ Capability authorization
→ Semantic owner decision
→ Sensitive operation
→ Verification
```

## Synthesis truth

Canonical final architecture belongs to:

```text
11-Synthesis/
├── System Synthesis.md
├── Logical Component Architecture.md
├── Deployment and Process Topology.md
├── Data and State Placement.md
├── Architecture Baseline Matrix.md
├── Specification Handoff.md
├── system-architecture.mmd
└── deployment-topology.mmd
```

Synthesis maps established owners/contracts into logical components and deployment boundaries. It must not silently override earlier canonical layers.

---

# Final architecture summary

## Build-time

```text
Microsoft-authorized Windows source
+
Signed/versioned SplitOS Build Manifest
+
SplitOS packages
+
Windows Component Matrix
↓
SplitOS Media Builder
↓
Typed supported transformations
↓
Baseline verification
↓
Prepared SplitOS Windows baseline
```

## Installed runtime

```text
Windows User Session
├── SplitOS Manager
├── SplitOS Game Launcher
└── SplitOS Runtime Host
        ├── runtime semantic modules
        ├── Windows Context Adapters
        ├── Game Client Adapters
        ├── Local State/Persistence
        ├── HTTPS → SplitOS Backend
        └── authenticated/authorized IPC
                 ↓
         SplitOS Privileged Broker
                 ↓
         bounded privileged Windows operations
```

External authorities remain external:

```text
Windows / Drivers / Devices
Steam / Epic / Xbox / Battle.net
Payment Provider
Microsoft Windows source ecosystem
Release signing/trust authority
```

---

# Product/runtime invariant

```text
Installed SplitOS
!= Paid entitlement
```

### FREE

```text
SplitOS Account
→ Entitlement FREE
→ ManagedRuntime=DISABLED
→ OperationalMode=NONE
→ normal Windows Desktop on SplitOS baseline
```

### PRO

```text
SplitOS Account
→ entitlement permits managed runtime
→ ManagedRuntime=ENABLED
→ WORK xor GAME
```

Premium runtime failure or entitlement uncertainty must not intentionally make base Windows unusable.

---

# Final logical component families

```text
Build Plane
├── Media Builder
├── Source Validator
├── Manifest Executor
└── Baseline Verifier

UX Plane
├── Manager / First Run
└── Game Launcher

Runtime Orchestration Plane
├── Runtime Access
├── Mode State / Transition / Policy
├── Application Lifecycle
├── System Contexts
├── Hardware
├── Game Library / Profiles / Optimization
├── Game Launch / Session / Shared Apps
├── Compatibility
├── Update / Recovery
├── Mutation Coordination
└── Observability

Adapter Plane
├── Windows adapters
└── per-Game-Client adapters

Privileged Plane
└── narrow Privileged Broker + allowlisted handlers

Persistence Plane
├── canonical/user state
├── durable transaction journal
├── projection caches
├── protected secrets
└── diagnostics

Backend Plane
├── Account/Auth
├── Entitlement
├── Subscription reconciliation
└── optional offline entitlement/release metadata services
```

---

# Core runtime rules

```text
User intent
!= committed state
!= transition state
!= actual Windows/device evidence
```

```text
command sent
!= command accepted
!= target observed
!= verification passed
!= canonical commit
```

```text
Mode Transition
or Update
or Recovery
```

must not independently execute conflicting machine mutations without coordination.

Direct game launch from Work remains composition:

```text
Work→Game
→ only after GAME commit
→ Managed Game Launch
```

`HANDOFF_ACCEPTED != GAME_RUNNING`.

---

# Failure/safety baseline

Safety priority:

```text
1. User data integrity
2. Windows bootability/base usability
3. Known coherent system state
4. Correct canonical SplitOS state
5. Managed runtime restoration
6. UX convenience
```

Rollback/recovery are themselves operations requiring actual-state verification.

A mixed partial Windows state is degraded evidence, not a new `HYBRID` operational mode.

---

# Trust baseline

```text
ordinary user process
!= Broker authority

signed executable
!= authorized capability

browser callback
!= account/payment/entitlement authority

HTTPS download
!= trusted release artifact

valid old signature
!= downgrade authorization

external metadata
!= privileged command
```

Privileged Broker is deliberately narrow and exposes no generic arbitrary admin-command product contract.

---

# Remaining explicit engineering decisions

Major OPEN items carried into Specification:

- local physical storage engine/schema/migrations;
- exact Runtime Host physical module packaging;
- IPC protocol/serialization/SDDL/caller checks/service account;
- OAuth/OIDC provider and redirect strategy;
- offline entitlement assertion format/TTL/device binding/clock handling;
- release manifest envelope/key hierarchy/rotation/revocation;
- update package/snapshot/rollback technology;
- supported system-wide default audio switching mechanism;
- exact Epic/Xbox/Battle.net mechanisms and stable Steam metadata strategy;
- Windows Component Matrix empirical classification;
- Microsoft-authorized Windows source acquisition/provenance model;
- performance thresholds and diagnostic retention;
- exact major-mutation concurrency primitive.

These remain explicit research/specification work and must not be silently resolved by implementation assumptions.

---

# Next phase

Detailed Specification should proceed using:

```text
11-Synthesis/Specification Handoff.md
```

Recommended specification families include:

```text
Runtime Process/Modules
Local IPC & Privileged Broker
Local Data/Persistence
Account/Auth/Entitlement
Mode Runtime
Windows Context Integrations
Game Client Adapters
Game Profile/Optimization
Game Launcher/Shared Apps UX
Builder/Component Matrix
Update/Recovery
Release Security/Key Management
Observability
Verification/Acceptance
```

If new engineering evidence contradicts the architecture, update the owning earlier A&D layer first and then re-synthesize; do not let detailed implementation become a hidden source of truth.