# SplitOS — Analysis & Design

Этот каталог содержит канонические аналитические модели SplitOS после Discovery, Concept и Requirements.

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

Порядок отражает reasoning dependency, а не жёсткий waterfall. Новое evidence может потребовать возврата к предыдущему слою.

---

## Current status

| Layer | Status | Canonical artifacts |
|---|---|---|
| `00-Boundaries` | READY | System Boundary Analysis, Build Pipeline, Installed Runtime Boundary, Component Classification |
| `01-Responsibilities` | READY | Responsibility Model |
| `02-Ownership` | READY | Ownership Model |
| `03-States` | READY | System State, Runtime Access, Mode Transition, Game Session |
| `04-Behavior` | READY | First Run/Runtime Access, Startup, Work→Game, Game Launch, Game→Work |
| `05-Data` | READY | Domain Model, Identity & Runtime Access, Configuration Model, Data Ownership and Lifecycle |
| `06-Interfaces` | READY | Interface Model, Internal Runtime Contracts, External Boundary Contracts, interface map |
| `07-Integrations` | READY | Integration Architecture, Windows Runtime Integration, Game Client Integration, Account/Payment/Builder/Update Integration, integration map |
| `08-Flows` | READY | Flow Model, First Run/Subscription, Work→Game, Managed Game Launch, Game→Work, Update/Recovery + sequence diagrams |
| `09-Failures` | NEXT | not started |
| `10-Trust` | NOT STARTED | — |
| `11-Synthesis` | NOT STARTED | — |

---

## Knowledge ownership rules

### Boundary truth

Canonical boundary ownership belongs to:

```text
00-Boundaries/System Boundary Analysis.md
```

Requirements-level `SplitOS System Context.md` provides high-level context and participants, but must not redefine A&D ownership boundaries independently.

### Responsibility truth

Canonical responsibility decomposition belongs to:

```text
01-Responsibilities/Responsibility Model.md
```

### Ownership truth

Canonical authority / evidence / writer / consumer relationships belong to:

```text
02-Ownership/Ownership Model.md
```

### State truth

Canonical runtime state semantics belong to:

```text
03-States/
```

`Runtime Access State Model.md` уточняет startup assumption: `WORK xor GAME` обязателен только при enabled managed runtime; FREE experience может стабильно иметь `OperationalMode = NONE` и обычный Windows Desktop.

UI labels, process presence и external-client state являются evidence/projections и не должны независимо переопределять canonical SplitOS state.

### Behavior truth

Canonical scenario behavior belongs to:

```text
04-Behavior/
```

Behavior defines semantic reaction to triggers/states but does not itself choose integration mechanisms or full multi-party timeline.

### Data truth

Canonical conceptual data meaning, configuration composition and lifecycle ownership belong to:

```text
05-Data/
├── Domain Model.md
├── Identity and Runtime Access Model.md
├── Configuration Model.md
└── Data Ownership and Lifecycle.md
```

Data layer deliberately distinguishes canonical product truth, policy/user intent, external projections, runtime evidence, transaction/recovery state and diagnostics.

Physical tables, storage engine, API schemas and concrete serialization formats are not yet canonical.

### Interface truth

Canonical semantic contracts belong to:

```text
06-Interfaces/
├── Interface Model.md
├── Internal Runtime Contracts.md
├── External Boundary Contracts.md
└── interface-map.mmd
```

```text
Interface
!= REST endpoint
!= Integration implementation
!= end-to-end Flow
```

### Integration truth

Canonical mechanism-level integration analysis belongs to:

```text
07-Integrations/
├── Integration Architecture.md
├── Windows Runtime Integration.md
├── Game Client Integration.md
├── Account Payment Builder and Update Integration.md
└── integration-map.mmd
```

Every mechanism is classified explicitly as VERIFIED / CANDIDATE / BEST_EFFORT / OPEN / REJECTED where applicable.

A missing supported mechanism remains OPEN rather than being replaced silently with undocumented behavior.

### Flow truth

Canonical end-to-end temporal composition belongs to:

```text
08-Flows/
├── Flow Model.md
├── First Run and Subscription Flow.md
├── Work to Game Flow.md
├── Managed Game Launch Flow.md
├── Game to Work Flow.md
├── Update and Recovery Flow.md
├── first-run-subscription.mmd
├── work-to-game.mmd
├── game-launch.mmd
├── game-to-work.mmd
└── update-recovery.mmd
```

Flow layer composes already-owned semantics:

```text
Trigger
→ Preconditions
→ Requests / Evidence
→ Owner decisions
→ Integration operations
→ Actual-state evidence
→ Verification
→ Commit / result
→ User-visible outcome
```

Critical flow rule:

```text
command sent
!= command accepted
!= operation submitted
!= target observed
!= verification passed
!= canonical commit
```

Flow does not redefine ownership/state. It shows how several owners and integrations cooperate over time.

---

## Current core system model

```text
Microsoft-authorized Windows source
        +
SplitOS Media Builder
        +
SplitOS Build Manifest / Packages
        +
Windows Component Matrix
        ↓
Prepared SplitOS Baseline
        ↓
Clean Installation
        ↓
Windows User
        ↓
SplitOS Account
        ↓
Entitlement
   ┌────┴────┐
   │         │
 FREE       PRO
   │         │
Windows     Managed Runtime
Desktop     ↓
            WORK xor GAME
```

Runtime integration topology:

```text
Interactive user session
├── SplitOS Manager
├── Game Launcher
└── SplitOS Runtime Host
        │
        ├── user-session Windows APIs
        ├── Game Client adapters
        ├── HTTPS → Account Backend
        └── secured local IPC
                 ↓
         SplitOS Privileged Broker
         Windows Service / Session 0
```

End-to-end mutation pattern:

```text
User intent
→ semantic owner/orchestrator
→ integration mechanism
→ immediate technical result
→ actual-state evidence
→ verification
→ canonical commit/result
→ visible UX
```

---

## Current canonical flow families

```text
FL-01 First Run / FREE-PRO / Upgrade
FL-02 Work → Game
FL-03 Managed Game Launch and Exit
FL-04 Game → Work
FL-05 Update and Recovery
```

Direct managed game launch from Work is intentionally composition, not a separate implementation:

```text
FL-02 Work → Game
+
FL-03 Managed Game Launch
```

Major mutating orchestration must be coordinated:

```text
Mode Transition
or Update
or Recovery
```

should own the machine-transition window rather than interleaving conflicting state mutations.

---

## Current mechanism baseline

### Windows

```text
User/session            → WTS / Win32 session APIs
Display read/apply      → QueryDisplayConfig / SetDisplayConfig family
Audio read/events       → Core Audio / MMDevice APIs
Default audio switching → OPEN until supported mechanism is validated
Power schemes           → PowrProf APIs
Process evidence        → Win32 process APIs
Service lifecycle       → Service Control Manager APIs
Privileged local IPC    → secured named pipes candidate
```

### Game Clients

```text
Stable SplitOS semantic contract
→ per-client adapter
→ Steam / Epic / Xbox / Battle.net specific mechanism
```

Support is capability-based; version-sensitive local metadata is never silently promoted to a stable public contract.

### Account / payment

```text
Runtime / Manager
→ HTTPS
→ SplitOS Account Backend
→ hosted checkout
→ Payment Provider
→ backend-validated payment evidence
→ Entitlement
```

### Builder / update

```text
Windows source
→ validate
→ Build Manifest
→ supported servicing mechanism
→ verify baseline
```

Update execution remains compatibility-gated and its exact technology/package format is still OPEN.

---

## Next analytical target

После `08-Flows` следующим слоем является `09-Failures`.

Failure analysis должен системно определить:

```text
failure source
failure class
who detects it
who owns recovery decision
retryable vs non-retryable
user-visible impact
rollback / safe fallback
persistence across reboot
observability requirement
```

Primary failure families:

```text
Account/backend unavailable
Entitlement stale/ambiguous
Privileged Broker unavailable
Windows platform operation rejected
Target state not reached after accepted operation
Mode transition rollback failure
Game Client unavailable/auth required
Game launch not confirmed
Hardware/display disappears mid-flow
Update apply/verification failure
Incomplete transaction after crash/reboot
Recovery target cannot be verified
```

`Failure != generic exception`: failure semantics must remain tied to owners, states and safe convergence rules.