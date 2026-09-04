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

Порядок отражает reasoning dependency, а не жёсткий waterfall.

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
| `07-Integrations` | READY | Integration Architecture, Windows Runtime Integration, Game Client Integration, Account/Payment/Builder/Update Integration |
| `08-Flows` | READY | First Run/Subscription, Work→Game, Managed Game Launch, Game→Work, Update/Recovery + sequence diagrams |
| `09-Failures` | READY | Failure Model, Runtime Failure Scenarios, Update/Recovery Failure Scenarios, Failure Handling Matrix, failure map |
| `10-Trust` | NEXT | not started |
| `11-Synthesis` | NOT STARTED | — |

---

# Knowledge ownership by layer

## Boundary truth

```text
00-Boundaries/
```

Defines build/runtime/external ownership boundaries.

## Responsibility truth

```text
01-Responsibilities/Responsibility Model.md
```

Defines what the system must own semantically.

## Ownership truth

```text
02-Ownership/Ownership Model.md
```

Defines authority, writers, consumers and evidence sources.

## State truth

```text
03-States/
```

Important invariant:

```text
FREE  → ManagedRuntime = DISABLED, OperationalMode = NONE
PRO   → ManagedRuntime = ENABLED, WORK xor GAME
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

Separates:

```text
Canonical product truth
Policy / user intent
External projections
Runtime evidence
Transaction / recovery state
Diagnostics
```

## Interface truth

```text
06-Interfaces/
```

Core contract rule:

```text
Consumer
→ request/query/event
→ canonical owner
→ semantic result
```

## Integration truth

```text
07-Integrations/
```

Mechanisms are classified explicitly:

```text
VERIFIED
CANDIDATE
BEST_EFFORT
OPEN
REJECTED
```

## Flow truth

```text
08-Flows/
```

Canonical end-to-end pattern:

```text
Trigger
→ Preconditions
→ Requests / Evidence
→ Owner decision
→ Integration operation
→ Actual-state evidence
→ Verification
→ Commit / result
→ User-visible outcome
```

Critical distinction:

```text
command sent
!= command accepted
!= operation submitted
!= target observed
!= verification passed
!= canonical commit
```

## Failure truth

Canonical failure semantics belong to:

```text
09-Failures/
├── Failure Model.md
├── Runtime Failure Scenarios.md
├── Update Recovery Failure Scenarios.md
├── Failure Handling Matrix.md
└── failure-map.mmd
```

Core failure rule:

```text
Failure evidence
→ owning responsibility
→ classify impact
→ choose response
→ apply response
→ verify resulting state
→ commit fallback/recovery result if needed
```

Failure handlers must not directly invent canonical state.

---

# Current core system model

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

Runtime topology:

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

---

# Current canonical flows

```text
FL-01 First Run / FREE-PRO / Upgrade
FL-02 Work → Game
FL-03 Managed Game Launch and Exit
FL-04 Game → Work
FL-05 Update and Recovery
```

Direct managed launch from Work is composition:

```text
FL-02 Work → Game
+
FL-03 Managed Game Launch
```

Major mutation coordination:

```text
Mode Transition
or Update
or Recovery
```

must not independently mutate conflicting machine state at the same time.

---

# Failure model summary

## Failure classes

```text
Request / precondition failure
Dependency unavailable
Unsupported capability
Missing/stale/contradictory external evidence
Operation rejected / technical failure
Partial application
Verification failure
Component crash / runtime loss
Persistence / durability failure
Interruption / reboot / power loss
Recovery failure
Trust / integrity validation failure
```

## Response classes

```text
Reject
Defer
Retry
Controlled fallback
Degraded continuation
Cancel to source state
Rollback
Recovery
Manual recovery / support required
```

## Severity

```text
S0 controlled negative outcome
S1 local feature failure
S2 operation failed but safe state known
S3 degraded system state
S4 recovery required
S5 manual recovery / bootability or data risk
```

Canonical safety priority:

```text
1. User data integrity
2. Windows bootability and base usability
3. Known coherent system state
4. Correct SplitOS canonical state
5. Managed runtime restoration
6. UX convenience
```

---

# Important failure invariants

```text
technical failure
!= permission to change canonical state

partial application
!= successful target

verification failure
→ target commit prohibited

source state remains canonical before target commit

mixed actual state
!= new HYBRID operational mode

rollback command sent
!= rollback successful

recovery command sent
!= recovery completed
```

Premium runtime failure must not intentionally make base Windows unusable merely because SplitOS cannot restore its own managed capabilities.

---

# Current mechanism baseline

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
→ Steam / Epic / Xbox / Battle.net mechanism
```

Version-sensitive local metadata is never promoted to stable external truth.

### Account / payment

```text
Runtime / Manager
→ HTTPS
→ SplitOS Account Backend
→ hosted checkout
→ Payment Provider
→ validated payment evidence
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

Target baseline/update identity changes only after semantic verification.

---

# Next analytical target

После `09-Failures` следующим слоем является `10-Trust`.

Trust analysis должен определить:

```text
who may call privileged interfaces
how local IPC caller identity is established
what data/artifacts are trusted
account authentication and token handling
entitlement evidence trust
payment evidence trust
update/package signatures
Build Manifest integrity
Windows source integrity
Game Client evidence trust level
secret storage
replay/tampering protection
least privilege boundaries
```

`Trust != Failure`:

- Failure описывает, что происходит при проблеме;
- Trust определяет, чему и кому система вообще разрешает верить до выполнения действия.
