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
| `08-Flows` | NEXT | not started |
| `09-Failures` | NOT STARTED | — |
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

`First Run and Runtime Access Behavior.md` определяет FREE/PRO branching, account onboarding и upgrade/downgrade behavior.

Behavior documents consume state/ownership knowledge; they do not redefine ownership.

### Data truth

Canonical conceptual data meaning, configuration composition and lifecycle ownership belong to:

```text
05-Data/
├── Domain Model.md
├── Identity and Runtime Access Model.md
├── Configuration Model.md
└── Data Ownership and Lifecycle.md
```

Data layer deliberately distinguishes:

```text
Canonical product truth
Policy / user intent
External projections
Runtime evidence
Transaction / recovery state
Diagnostics
```

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

Interface layer defines:

```text
Provider / semantic owner
Consumer
Request / query / event meaning
Input / output semantics
Errors / rejection reasons
Temporal / verification semantics
Ownership boundary
```

It does not decide transport technology automatically.

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

Integration layer may select or reject technical mechanism families, but must preserve semantic ownership from earlier layers.

Every mechanism is classified explicitly:

```text
VERIFIED
CANDIDATE
BEST_EFFORT
OPEN
REJECTED
```

A missing supported mechanism remains OPEN rather than being replaced silently with undocumented behavior.

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

Runtime integration topology now has an explicit user-session / privileged split:

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

Critical integration rule:

```text
UI request
→ semantic owner/orchestrator
→ integration mechanism
→ immediate technical result
→ actual-state evidence
→ verification
→ canonical result
```

Not:

```text
API call returned success
= product target reached
```

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

Support is capability-based; version-sensitive local metadata is never silently promoted to a public stable contract.

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

### Builder

```text
Windows source
→ validate
→ Build Manifest
→ DISM/offline servicing where applicable
→ verify baseline
```

---

## Next analytical target

После `07-Integrations` следующим слоем является `08-Flows`.

Flow layer должен связать уже определённые:

```text
state
behavior
owned data
interface contract
integration mechanism
```

в end-to-end sequences.

Primary flows:

```text
First Run + Account
FREE → PRO Upgrade
Startup PRO → Mode Selection
Work → Game
Managed Game Launch
Game Exit → Launcher
Game → Work
Hardware/Display Change
Update
Recovery
Builder / Clean Install
```

`Flow != Interface` и `Flow != State Machine`: flow показывает сквозное взаимодействие нескольких owners/integrations во времени.