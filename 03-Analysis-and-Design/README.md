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
| `03-States` | READY | System State, Mode Transition, Game Session |
| `04-Behavior` | READY | Startup, Work→Game, Game Launch, Game→Work |
| `05-Data` | NEXT | not started |
| `06-Interfaces` | NOT STARTED | — |
| `07-Integrations` | NOT STARTED | — |
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

UI labels, process presence and external-client state are evidence or projections and must not independently redefine canonical SplitOS state.

### Behavior truth

Canonical scenario behavior belongs to:

```text
04-Behavior/
```

Behavior documents consume state/ownership knowledge; they do not redefine ownership.

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
Installed SplitOS Runtime
        ↓
WORK xor GAME
```

Runtime state is not one enum. It is modeled as orthogonal dimensions:

```text
System Session Lifecycle
+
Committed Operational Mode
+
Mode Transition Lifecycle
+
Game Session Lifecycle
+
Recovery Lifecycle
```

---

## Next analytical target

После `04-Behavior` следующим слоем является `05-Data`.

Data analysis должен начинаться с ownership и meaning, а не с таблиц/БД:

```text
OWNERSHIP
↓
CONCEPTUAL MODEL
↓
LOGICAL MODEL
↓
PHYSICAL STORAGE
```
