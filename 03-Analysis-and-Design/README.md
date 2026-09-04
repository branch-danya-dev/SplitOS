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
| `10-Trust` | READY | Trust Model, Local Privilege & IPC, Identity/Entitlement/Secrets, Artifact/Build/Update Trust, External Evidence Trust, Security Control Matrix, trust map |
| `11-Synthesis` | NEXT | not started |

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

```text
09-Failures/
```

Core failure rule:

```text
Failure evidence
→ owning responsibility
→ classify impact
→ choose response
→ apply response
→ verify resulting state
→ commit fallback/recovery result if proven
```

Failure handlers must not invent canonical state.

## Trust truth

Canonical trust semantics belong to:

```text
10-Trust/
├── Trust Model.md
├── Local Privilege and IPC Trust.md
├── Identity Entitlement and Secret Trust.md
├── Artifact Build and Update Trust.md
├── External Evidence Trust.md
├── Security Control Matrix.md
└── trust-map.mmd
```

Trust answers:

```text
who is the subject/issuer
→ how identity/provenance is established
→ how integrity/freshness/context are validated
→ what capability is authorized
→ what action may follow
```

Core rule:

```text
trusted for one claim/capability
!= globally trusted component
```

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
        └── authenticated + authorized local IPC
                 ↓
         SplitOS Privileged Broker
         Windows Service / Session 0
```

The Privileged Broker is a narrow machine-mutation boundary, not a general administrator API.

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

# Trust model summary

## Trust zones

```text
Windows Platform Authority
Interactive User Session
Privileged Broker
Local Persistent SplitOS State
SplitOS Backend
Release / Build Trust Domain
External Authorities
```

## Local privilege

```text
UI
→ Runtime Host semantic owner
→ bounded privileged capability
→ explicit Named Pipe ACL / caller token-session validation candidate
→ Privileged Broker
→ bounded OS mutation
→ actual-state verification
```

No generic `RunCommand`, arbitrary PowerShell, raw registry/service mutation contract is allowed.

## Identity / entitlement

```text
Windows User
→ SplitOS Account auth via external browser/native-app flow candidate
→ server-issued tokens
→ protected local secret storage (DPAPI candidate)
→ server/offline entitlement evidence
→ ManagedRuntimeAccessDecision
```

```text
FREE/PRO
!= editable local setting
```

Offline premium access requires bounded verifiable evidence, not `cachedPro=true`.

## Artifact trust

```text
release authority
→ signed manifest / signed or digest-bound artifact
→ compatibility validation
→ protected staging
→ privileged apply
→ read-back verification
→ baseline commit
```

Authenticode/WinVerifyTrust is the current Windows-native binary-signature mechanism family. Structured manifests require a separate versioned signed format.

## External evidence

```text
External source
→ bounded adapter/parser
→ validate + normalize + freshness metadata
→ semantic owner interpretation
→ canonical state/projection only if justified
```

Game Client/browser/device evidence never becomes direct privileged command input.

---

# Security invariants

```text
ordinary user process
!= privileged Broker authority

signed executable
!= authorized capability

browser callback
!= authenticated account/payment/entitlement result

HTTPS download
!= trusted release artifact

valid old signature
!= downgrade authorization

external client metadata
!= privileged command

trust validation failure
→ deny sensitive capability
→ preserve base Windows usability where possible
```

v1 explicitly does not promise resistance to an unrestricted hostile local Administrator/kernel/firmware compromise.

---

# Current mechanism/security baseline

### Windows

```text
User/session            → WTS / Win32 session APIs
Display read/apply      → QueryDisplayConfig / SetDisplayConfig family
Audio read/events       → Core Audio / MMDevice APIs
Default audio switching → OPEN
Power schemes           → PowrProf APIs
Process/service evidence→ Win32 / SCM
Privileged local IPC    → Named Pipe candidate + explicit ACL/caller validation
Local user secret       → DPAPI candidate
Binary provenance       → Authenticode / WinVerifyTrust candidate
```

### Game Clients

```text
stable SplitOS semantic contract
→ bounded per-client adapter/parser
→ external client evidence
```

Version-sensitive local metadata remains BEST_EFFORT/CANDIDATE and is never promoted to privileged authority.

### Account / payment

```text
Runtime / Manager
→ external browser auth candidate + PKCE
→ HTTPS SplitOS Account Backend
→ hosted checkout
→ Payment Provider
→ backend-validated payment evidence
→ Entitlement
```

### Builder / update

```text
validated Windows source
→ signed Build/Update Manifest
→ exact artifact binding
→ supported servicing mechanism
→ verification
→ supported baseline identity
```

---

# Next analytical target

После `10-Trust` следующим и финальным A&D layer является `11-Synthesis`.

Synthesis должен собрать предыдущие решения в целостную implementable architecture view:

```text
system/component decomposition
runtime deployment topology
process/service boundaries
canonical component responsibilities
owned state/data
internal/external contracts
trust boundaries
major end-to-end flows
failure/recovery paths
build/update architecture
open implementation decisions
```

Synthesis не должен заново придумывать ownership/state. Его задача — собрать уже доказанные слои в единую архитектурную модель, пригодную для Specification и дальнейшей реализации.