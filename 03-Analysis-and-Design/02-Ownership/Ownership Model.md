# SplitOS — Ownership Model

## 1. Purpose

Документ определяет канонический ownership значимых решений, состояний и системных фактов SplitOS после фиксации Responsibility Model.

Ownership отвечает на пять вопросов:

```text
Who decides?
Who stores canonical state/rule?
Who may change it?
Who consumes it?
Who verifies correctness?
```

Ключевой принцип:

> У каждого значимого системного факта или решения должен быть один canonical owner, даже если evidence приходит из нескольких источников и результат потребляется многими зонами.

Этот документ не назначает ownership будущим классам, процессам, Windows services или базам данных. Owner здесь — responsibility zone.

---

# 2. Ownership rules

## OWN-RULE-001 — Evidence is not ownership

Внешняя система может сообщать факт, но не обязана владеть внутренним решением SplitOS.

```text
Windows / Driver evidence
        ↓
SplitOS interpretation
        ↓
SplitOS canonical decision
```

Пример:

```text
Driver reports 4K / 60 Hz / HDR capability
→ evidence

Hardware Context Evaluation
→ canonical SplitOS hardware snapshot

Game Profile / Optimization Policy
→ consume snapshot
```

---

## OWN-RULE-002 — Desired state and actual state have different owners

SplitOS может владеть желаемым состоянием, но не фактическим физическим состоянием платформы.

```text
Desired display state
→ SplitOS

Actual OS display state
→ Windows / driver evidence

Physical capability
→ device / driver evidence
```

SplitOS обязан верифицировать результат после применения изменения.

---

## OWN-RULE-003 — Consumers must not create competing truth

Если canonical owner уже определён, consumer не должен самостоятельно пересчитывать тот же системный факт как отдельную истину.

---

## OWN-RULE-004 — User intent and system state are different facts

Пользователь является authority для намерения:

```text
"Switch to Game Mode"
```

Но SplitOS является canonical owner состояния:

```text
Current committed operational mode = GAME
```

Пользовательский запрос сам по себе не означает, что transition успешно завершён.

---

## OWN-RULE-005 — External product authority remains external

SplitOS не присваивает себе ownership состояния, которое принадлежит внешнему продукту:

```text
Game license        → Game platform
Game installation   → Game Client authority
Windows license     → Microsoft / Windows licensing
Driver capability   → Driver / device
Application data    → Application
```

SplitOS может хранить projection/cache этого состояния, но не competing canonical truth.

---

# 3. Ownership overview

| Knowledge / Decision | Canonical owner | Primary evidence | Main consumers | Verification |
|---|---|---|---|---|
| Supported Windows base | Compatibility Management | Microsoft Windows release/source metadata + SplitOS tests | Distribution Engineering, Update Orchestration | compatibility validation |
| Windows component classification | Distribution Engineering | component inventory, dependency analysis, tests | Builder, Compatibility Management | build/regression tests |
| SplitOS build manifest | Distribution Engineering | release definition | Builder, Recovery, Compatibility | deterministic build validation |
| SplitOS account identity | Product Identity & Entitlement | account/auth evidence | Runtime, profiles, update/support | identity validation |
| SplitOS entitlement | Product Identity & Entitlement | subscription/license evidence | feature access, update/support | entitlement reconciliation |
| User mode intent | User | explicit user action | Mode Intent & Active State | accepted request |
| Current committed mode | Mode Intent & Active Mode State | transition outcome | all runtime policy consumers | invariant/state verification |
| Target mode policy | Mode Policy | canonical policy definitions | Transition, app lifecycle, system contexts | desired-vs-actual validation |
| Transition state/outcome | Mode Transition Coordination | action results + blockers + confirmations | Mode State, Recovery, UI | transition checks |
| Application class | Application Lifecycle Policy | supported metadata/integration/user configuration where allowed | Transition, Game Launch, Mode Policy | classification validation |
| Desired application lifecycle | Application Lifecycle Policy | mode policy + classification | Transition/runtime execution | process/service state readback |
| Desired display context | Display Context | user/profile/mode selection | Transition, Game Profiles, Game Launch | Windows/driver actual state |
| Desired audio context | Audio Context | user/mode profile | Transition | Windows/audio actual state |
| Selected input profile | Input Context | user/Game Profile | Game UX, Game Launch | device/input state evidence |
| Desired power/resource policy | Power / Resource Context | mode policy | Transition/runtime | Windows actual state / metrics |
| Unified SplitOS game-library representation | Game Library Representation | external client library evidence | Game Mode UX, Game Launch | reconciliation with client authority |
| Game installation truth | External Game Client | client/platform state | SplitOS Game Library | external client evidence |
| Game license truth | External Game Client / platform | platform account/license state | Game Launch | platform response |
| Game Profile | Game Profiles | user choices + SplitOS recommendations | Launcher, Optimization, Launch | profile validity checks |
| Hardware snapshot | Hardware Context Evaluation | Windows/driver/device evidence | Game Profiles, Optimization, Launch | refresh/readback |
| Optimization recommendation | Game Optimization Policy | Game Profile + hardware/display + game knowledge | Game Launch, UX | benchmark/config validation |
| User game-setting override | User, represented canonically by Game Profiles | game config/user action evidence | Optimization Policy | config reconciliation |
| Game Mode UX state | Game Mode UX | runtime/game lifecycle evidence | User | UI/runtime consistency checks |
| Shared App Game representation | Shared App Experience | app state + mode policy | Game Mode UX | app/window state verification |
| Supported external version/combination | Compatibility Management | external version evidence + tests | Update, Distribution, Integrations | regression/compatibility suite |
| Update eligibility | Product Identity & Entitlement + Update Orchestration, separated by meaning | entitlement + compatible release state | Update flow | entitlement and compatibility checks |
| Release compatibility decision | Compatibility Management | test evidence | Update Orchestration, Distribution | signed/recorded validation result |
| Update execution state | Update Orchestration | updater/platform operation results | Recovery, UI, Diagnostics | post-update validation |
| Recovery target | Recovery Coordination | last-known-good state + failure evidence | runtime/update flows | recovered-state validation |
| Diagnostic record of SplitOS actions | Observability & Diagnostics | events from owning zones | support, recovery, engineering | correlation/completeness checks |

Where two areas appear in one row, they own different facts. Example: Product Identity owns whether the account is entitled; Update Orchestration owns whether a particular compatible release is currently offered/applied.

---

# 4. Distribution ownership

## 4.1 Supported Windows base

### Decision owner

```text
Compatibility Management
```

### Evidence

```text
Microsoft source metadata
Windows build identity
component inventory
regression results
security/compatibility test evidence
```

### Canonical fact

```text
Windows Base X is SUPPORTED | UNSUPPORTED for SplitOS Release Y
```

Distribution Engineering consumes this decision and must not independently redefine compatibility.

---

## 4.2 Windows Component Matrix

### Canonical owner

```text
Distribution Engineering
```

### Owns

```text
Component
→ REMOVE | DISABLE | MODE_MANAGED | KEEP
```

plus required rationale and build-time action.

Compatibility Management provides evidence about whether the decision remains safe for a given release, but does not create a second matrix.

### Writers

Changes may be accepted only through validated distribution engineering decisions based on tests/evidence.

### Consumers

```text
SplitOS Build Pipeline
Mode Policy for MODE_MANAGED entries
Compatibility Management
Recovery / release validation
```

---

## 4.3 SplitOS Build Manifest

Canonical owner:

```text
Distribution Engineering
```

It is the canonical definition of what constitutes the build-time SplitOS baseline for a release.

Builder is an executor/consumer, not the semantic owner of the manifest.

---

# 5. Identity and entitlement ownership

## 5.1 SplitOS Account

Canonical owner:

```text
Product Identity & Entitlement
```

External identity provider, payment processor or future licensing service may provide evidence, but SplitOS must own its product-level user identity mapping.

## 5.2 Entitlement

Canonical SplitOS fact:

```text
User U has entitlement E in state S
```

Owner:

```text
Product Identity & Entitlement
```

Consumers:

```text
Game Mode feature access
premium capabilities
update/support channel eligibility
future cloud features
```

Important distinction:

```text
Payment evidence
≠
SplitOS entitlement state
```

A future payment provider may prove a transaction, but SplitOS owns the resulting product entitlement model.

## 5.3 Windows licensing

Owner remains external:

```text
Microsoft / Windows licensing model
```

SplitOS must not derive its own competing Windows-license truth.

---

# 6. Mode ownership

## 6.1 User Mode Intent

Authority:

```text
User
```

Examples:

```text
Select WORK
Select GAME
Cancel transition
Return to Work
```

SplitOS records/consumes intent but does not own the user's decision.

---

## 6.2 Current committed operational mode

Canonical owner:

```text
Mode Intent & Active Mode State
```

Canonical values are modeled later, but must obey:

```text
WORK xor GAME
```

Transition Coordination may be executing a transition, but it must not independently publish a competing committed mode truth.

### Writers

Only validated transition completion/recovery logic may change committed mode state.

### Consumers

```text
Mode Policy
Application Lifecycle Policy
System Context Policies
Game Mode UX
Game Launch Orchestration
Diagnostics
```

---

## 6.3 Transition state

Canonical owner:

```text
Mode Transition Coordination
```

Owns facts such as:

```text
transition requested
pre-flight in progress
blocked
awaiting user
applying target state
rolling back
completed
```

Exact state machine belongs to States analysis.

### Evidence sources

- application/process state;
- system-context state;
- user confirmations;
- operation results;
- mode-policy target.

### Consumer

Mode State consumes only committed transition result when determining canonical active mode.

---

# 7. Mode policy and actual platform state

## 7.1 Desired mode state

Canonical owner:

```text
Mode Policy
```

For each mode it defines what should be true.

Example:

```text
Phone Link / Cross-Device
WORK → AVAILABLE
GAME → INACTIVE
```

Mode Policy does not own whether Windows actually reached that state.

## 7.2 Actual Windows state

Evidence authority:

```text
Windows
```

For driver/device-backed facts:

```text
Windows + Driver + Device
```

SplitOS must read back actual state and compare it with desired state.

Canonical SplitOS interpretation of whether the operation succeeded belongs to the responsibility that initiated/coordinates that managed operation, not to Windows itself.

---

# 8. Application ownership

## 8.1 Application classification

Canonical owner:

```text
Application Lifecycle Policy
```

Canonical SplitOS classes:

```text
WORK
GAME
GAME_CLIENT
SHARED
SYSTEM
```

External metadata may provide evidence, but cannot independently determine SplitOS classification semantics.

## 8.2 Application internal state

External authority:

```text
corresponding application
```

SplitOS must not assume that process existence fully represents application business/data state.

Example:

```text
WINWORD.EXE running
≠
proof that document is safely saved
```

## 8.3 Desired lifecycle action

Owner:

```text
Application Lifecycle Policy
```

Transition Coordination consumes the desired action and coordinates execution.

This prevents transition logic from becoming the hidden owner of application policy.

---

# 9. System context ownership

## 9.1 Display

### Desired display context owner

```text
Display Context
```

Owns SplitOS preference/desired state such as:

```text
target display
resolution
refresh rate
HDR intention
VRR intention
fallback policy
```

### Actual state evidence

```text
Windows / GPU driver / display-reported capability
```

### Consumers

```text
Mode Transition
Game Profiles
Hardware Context Evaluation
Game Optimization
Game Launch
```

---

## 9.2 Audio

Desired state owner:

```text
Audio Context
```

Actual state evidence:

```text
Windows Audio / driver / device
```

---

## 9.3 Input

SplitOS input-context owner:

```text
Input Context
```

Owns:

```text
selected SplitOS input profile
system-navigation behavior
controller-first UX context
```

Gameplay input authority remains:

```text
Game
```

Device capability evidence remains:

```text
Windows / driver / device
```

---

## 9.4 Power / Resource Context

Desired policy owner:

```text
Power / Resource Context
```

Actual platform state evidence:

```text
Windows / platform telemetry
```

Performance measurements are evidence, not ownership of policy.

---

# 10. Game library ownership

## 10.1 External installation truth

Canonical external authority:

```text
External Game Client
```

for the client's own library/install state.

SplitOS should reconcile against this source.

## 10.2 SplitOS unified library

Canonical owner:

```text
Game Library Representation
```

Owns the internal projection:

```text
Game
↔ external Game Client
↔ SplitOS metadata
↔ SplitOS launch eligibility
```

It may cache external facts, but cached data is not allowed to silently override fresher client evidence.

## 10.3 Game license

External authority:

```text
corresponding Game Client / platform
```

SplitOS Launch Orchestration is consumer only.

---

# 11. Game Profile ownership

Canonical owner:

```text
Game Profiles
```

Game Profile is SplitOS-owned knowledge even when some fields originate from external evidence.

Example:

```text
Game Profile
├── target display       ← SplitOS choice
├── input profile        ← SplitOS/user choice
├── target FPS           ← SplitOS policy/user override
├── graphics settings    ← SplitOS recommendation/user override
├── hardware snapshot    ← Hardware Context evidence/reference
└── client relation      ← Game Library evidence/reference
```

No other zone should keep a separate canonical copy of the same profile.

---

# 12. Hardware ownership

## 12.1 Physical capability

External authority:

```text
Hardware / Driver / Windows-reported capability
```

## 12.2 SplitOS hardware snapshot

Canonical owner:

```text
Hardware Context Evaluation
```

It owns the interpreted snapshot used by SplitOS and the rule deciding whether change is meaningful enough to invalidate/re-evaluate profiles.

Thus:

```text
raw hardware evidence
→ Hardware Context Evaluation
→ canonical SplitOS snapshot
→ Game Profiles / Optimization / Launch
```

---

# 13. Optimization ownership

Canonical owner of recommendation policy:

```text
Game Optimization Policy
```

Evidence:

```text
Hardware Context
Display Context
Game Profile
supported game-setting knowledge
performance measurements
```

Canonical output:

```text
recommended supported game configuration
```

## User override

User remains authority for the explicit choice to override.

The canonical representation of that override should be owned by:

```text
Game Profiles
```

Optimization Policy must consume that fact rather than inventing its own independent override state.

Exact precedence and recalculation behavior remains for Behavior/States analysis.

---

# 14. Game launch ownership

Canonical owner of the end-to-end launch transaction:

```text
Game Launch Orchestration
```

It owns the lifecycle/result of a SplitOS-managed launch, but not the source truths it consumes.

```text
Mode readiness         → Mode State
Game installation      → Game Client evidence
Game license           → Game Client/platform
Game Profile           → Game Profiles
Hardware snapshot      → Hardware Context
Optimization result    → Game Optimization Policy
Display desired state  → Display Context
Input desired state    → Input Context
```

Game Launch Orchestration coordinates these owners; it must not duplicate their canonical state.

---

# 15. Game Mode UX ownership

Canonical owner of gaming presentation/navigation state:

```text
Game Mode UX
```

Examples:

```text
current launcher surface
selected game card
active Game Mode navigation context
return-to-launcher presentation
```

It is a consumer of domain state, not authority for:

```text
active mode
entitlement
game installation
hardware capability
Game Profile contents
```

The UI must not become canonical system state merely because it displays it.

---

# 16. Shared App ownership

SplitOS canonical owner of Game Mode representation:

```text
Shared App Experience
```

External application retains ownership of:

```text
application data
account state
internal UI/business state
```

SplitOS owns only its managed relationship:

```text
SHARED eligibility
presentation state
placement
active-limit policy
Game Mode lifecycle treatment
```

---

# 17. Compatibility ownership

Canonical owner:

```text
Compatibility Management
```

Owns decisions such as:

```text
Windows build X supported for SplitOS R
Game Client version Y supported
Driver range Z tested/recommended
Component classification remains valid
```

Evidence may come from:

```text
vendor releases
regression tests
user diagnostics
lab measurements
prototype results
```

Evidence does not directly mutate compatibility status without canonical decision.

---

# 18. Update ownership

## 18.1 Patch source

External authority:

```text
Microsoft / corresponding vendor
```

## 18.2 Compatibility decision

Owner:

```text
Compatibility Management
```

## 18.3 SplitOS release/update execution

Owner:

```text
Update Orchestration
```

Owns:

```text
update preconditions
release selection among compatible releases
migration execution
update transaction state
post-update validation request
update result
```

## 18.4 Update entitlement

Owner:

```text
Product Identity & Entitlement
```

Thus:

```text
Entitled?
→ Identity & Entitlement

Compatible?
→ Compatibility Management

Should/apply now and what happened?
→ Update Orchestration
```

These must not collapse into one ambiguous "Update owner".

---

# 19. Recovery ownership

Canonical owner:

```text
Recovery Coordination
```

It owns the recovery decision after receiving failure evidence.

Inputs:

```text
transition failure
update failure
baseline drift
runtime inconsistency
last-known-good evidence
user data state
```

Output:

```text
selected safe recovery target
recovery lifecycle/result
```

Mode Transition and Update Orchestration may initiate recovery request, but should not each implement a competing global recovery truth.

---

# 20. Observability ownership

Observability & Diagnostics owns the canonical diagnostic representation of SplitOS-controlled actions and correlations.

It does **not** own the business/system facts merely because it logs them.

Example:

```text
Diagnostic log says mode=GAME
≠
diagnostics owns active mode
```

Active mode remains owned by Mode Intent & Active Mode State.

Diagnostics records evidence/projection sufficient to explain:

- what decision was made;
- which owner made it;
- what operation was attempted;
- what actual evidence was observed;
- what result was determined.

---

# 21. Write authority principles

A canonical owner may allow other zones to request changes without granting them direct write ownership.

Example:

```text
Game Mode UX
→ requests target display change

Display Context
→ validates and changes canonical desired display state
```

Similarly:

```text
Game launch detects profile invalidity
→ requests re-evaluation

Game Profiles
→ updates canonical profile state
```

This prevents shared mutable truth.

---

# 22. Verification ownership

The owner of a desired state is responsible for defining how correctness is verified, even if evidence comes from another system.

Pattern:

```text
Decision owner
        ↓
requests platform change
        ↓
external executor
        ↓
actual-state evidence
        ↓
owner/coordination validates result
```

Examples:

```text
Display Context
→ desired display state
→ Windows applies
→ Windows/driver reports actual state
→ SplitOS verifies match
```

```text
Update Orchestration
→ applies release
→ platform/reboot operations occur
→ post-update evidence
→ SplitOS validates resulting release state
```

---

# 23. Ownership conflicts explicitly avoided

The model rejects the following competing truths:

```text
Game Launcher owning active mode
Game Launcher owning installed-game truth
UI owning entitlement
Builder owning compatibility decision
Transition Coordinator owning mode policy
Optimization engine owning hardware capability
Game Profile owning physical display capability
Diagnostics owning operational state
External payment provider owning SplitOS access semantics
Windows Update owning SplitOS compatibility policy
```

---

# 24. Open ownership questions

The following require deeper Behavior / States / Data analysis:

1. Where is canonical committed mode state persisted physically?
2. Is transition state durable across reboot/crash, and what facts must survive?
3. What exact object owns user game-setting override precedence?
4. How long is a Hardware Context snapshot valid?
5. How is stale Game Client library evidence detected/reconciled?
6. Which entitlement facts must be available offline and for how long?
7. What exact evidence establishes a last-known-good baseline/release?
8. Which component-classification changes require a new clean baseline versus an in-place update?
9. Which diagnostic facts are durable enough to support recovery without becoming canonical runtime truth?

These questions do not invalidate ownership; they determine physical storage, state lifecycle and interfaces later.

---

# 25. Result

Canonical reasoning chain for SplitOS is now:

```text
Evidence
    ↓
Responsibility owner
    ↓
Canonical decision / state / rule
    ↓
Consumers
    ↓
Verification against actual evidence
```

Examples:

```text
Windows/driver display evidence
→ Hardware Context Evaluation
→ SplitOS hardware snapshot
→ Game Profile / Optimization
```

```text
User requests GAME
→ Mode Transition Coordination
→ successful transition result
→ Mode Intent & Active Mode State
→ committed GAME
→ Mode Policy consumers
```

```text
Microsoft patch released
→ Compatibility Management
→ supported/unsupported decision
→ Update Orchestration
→ validated SplitOS release
```

With ownership defined, the next analytical layer may safely model Behavior, States and Data without allowing several areas to invent competing versions of the same system truth.
