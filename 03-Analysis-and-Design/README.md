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
| `07-Integrations` | NEXT | not started |
| `08-Flows` | NOT STARTED | — |
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

`Runtime Access State Model.md` уточняет прежнюю startup assumption: `WORK xor GAME` обязателен только при enabled managed runtime; FREE experience может стабильно иметь `OperationalMode = NONE` и обычный Windows Desktop.

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

It does **not** decide transport technology automatically.

```text
Interface
!= REST endpoint
!= Integration implementation
!= end-to-end Flow
```

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

Runtime state is modeled as orthogonal dimensions:

```text
Windows Session
+
SplitOS Account Context
+
Entitlement State
+
Managed Runtime Access
+
Committed Operational Mode
+
Mode Transition Lifecycle
+
Game Session Lifecycle
+
Recovery Lifecycle
```

Data is modeled as owned semantic layers:

```text
Release/Baseline knowledge
+
Installed baseline identity
+
Windows user ↔ SplitOS account association
+
Entitlement / runtime access
+
User/Mode/Game configuration
+
External projections/evidence
+
Transaction/Recovery data
```

Interfaces preserve these ownership boundaries:

```text
Consumer
→ request/query/event contract
→ canonical owner
→ validated semantic result
```

External evidence follows:

```text
External authority
→ adapter/interface boundary
→ SplitOS owner interpretation
→ canonical state or projection
```

---

## Next analytical target

После `06-Interfaces` следующим слоем является `07-Integrations`.

Integration analysis должен определить конкретные mechanisms за уже существующими semantic contracts:

```text
Windows APIs / services / privileged operations
Game Client adapters
Account authentication/backend channel
Payment checkout/evidence path
Microsoft source/update inputs
local IPC/process boundaries
```

При этом Integration не должна менять semantic ownership только потому, что конкретная технология удобнее.

После Integrations `08-Flows` свяжет отдельные contracts/integrations в end-to-end последовательности.
