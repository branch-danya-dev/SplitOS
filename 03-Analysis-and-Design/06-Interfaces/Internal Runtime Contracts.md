# SplitOS — Internal Runtime Contracts

## 1. Purpose

Документ описывает ключевые semantic interfaces между внутренними responsibility zones SplitOS Runtime.

Это **не список будущих сервисов** и не API specification.

Каждый контракт здесь определяет:

```text
кто предоставляет смысл
кто потребляет
что запрашивается/читается
какой результат считается значимым
кто остаётся владельцем истины
```

---

# 2. Identity and runtime access

## IF-ID-001 — Resolve SplitOS Account Context

**Category:** Query / reconciliation

**Provider:** Product Identity & Entitlement

**Consumers:** First Run Experience, SplitOS Manager, Runtime Access Evaluation

**Purpose:** определить SplitOS account context, ассоциированный с текущим Windows user context.

### Input

```text
WindowsUserContext
local association evidence
available authentication/session evidence
```

### Output

```text
ACCOUNT_RESOLVED
ACCOUNT_REQUIRED
ACCOUNT_REAUTH_REQUIRED
ACCOUNT_CONTEXT_UNAVAILABLE
```

При `ACCOUNT_RESOLVED` возвращается canonical SplitOS account reference.

### Ownership

Windows user identity остаётся Windows-owned context.
SplitOS account mapping принадлежит Product Identity & Entitlement.

---

## IF-ID-002 — Resolve Entitlement

**Category:** Query / reconciliation

**Provider:** Product Identity & Entitlement

**Consumers:** Runtime Access Evaluation, SplitOS Manager, Update eligibility, premium feature gates

### Input

```text
SplitOSAccount
current product policy context
available backend/cache evidence
```

### Output

Conceptually:

```text
FREE
PRO_ACTIVE
PRO_GRACE
EXPIRED
UNKNOWN_OFFLINE
ACCOUNT_INVALID
```

Exact commercial plan names remain product policy.

### Rule

Payment evidence не возвращается consumer как entitlement truth.

---

## IF-ACCESS-001 — Evaluate Managed Runtime Access

**Category:** Query / decision

**Provider:** Product Identity & Entitlement / Runtime Access decision responsibility

**Consumers:** startup flow, mode selection, SplitOS Manager, Game launch interception

### Input

```text
account context
entitlement state
offline policy
local installation state
```

### Output

```text
ENABLED
DISABLED
DEGRADED
REAUTH_REQUIRED
```

### Consequence

```text
DISABLED
→ OperationalModeState = NONE is valid
→ normal Windows desktop remains usable

ENABLED
→ managed runtime may enter WORK xor GAME
```

---

## IF-ACCESS-002 — Runtime Access Changed

**Category:** Event

**Provider:** Runtime Access decision owner

**Consumers:** Mode State, SplitOS Manager, Game Launcher, policy consumers

### Event fact

```text
previous access
new access
reason category
changed at
```

### Rule

Event не определяет автоматически конкретный downgrade transition behavior. Это consumer flow responsibility.

---

# 3. Mode state and transitions

## IF-MODE-001 — Request Operational Mode

**Category:** Command

**Provider:** Mode Intent & Active Mode State

**Consumers:** Mode Selection UX, SplitOS Manager, Game Launcher, direct managed game launch scenario

### Input

```text
target = WORK | GAME
initiator
optional scenario context
```

### Preconditions

- Managed Runtime Access = `ENABLED`;
- target отличается от committed mode либо допустим как no-op;
- нет conflict с уже активным transition.

### Immediate result

```text
ACCEPTED
ALREADY_IN_TARGET
NOT_ENTITLED
INVALID_STATE
TRANSITION_IN_PROGRESS
REJECTED_BY_POLICY
```

### Critical rule

`ACCEPTED` не меняет committed mode.

---

## IF-MODE-002 — Read Committed Operational Mode

**Category:** Query

**Provider:** Mode Intent & Active Mode State

**Consumers:** практически все runtime policy consumers

### Output

```text
NONE | WORK | GAME
```

### Rule

Consumer не должен выводить committed mode из UI, process list или transition target.

---

## IF-MODE-003 — Operational Mode Committed

**Category:** Event

**Provider:** Mode Intent & Active Mode State

**Consumers:** Policy, Game Mode UX, Manager, Observability, app/system contexts

### Payload semantics

```text
previous committed mode
new committed mode
transition correlation
commit timestamp
```

Только этот или эквивалентный owner-produced fact означает semantic mode commit.

---

## IF-TRANS-001 — Start Mode Transition

**Category:** Transaction command

**Provider:** Mode Transition Coordination

**Consumer:** Mode Intent & Active Mode State after accepted user/system intent

### Input

```text
source committed mode
target mode
initiator/scenario
```

### Output

```text
TRANSITION_STARTED
PRECONDITION_FAILED
CONFLICT
```

### Ownership

Transition Coordination владеет transition lifecycle, но не committed operational mode.

---

## IF-TRANS-002 — Resolve Transition Blocker

**Category:** Command

**Provider:** Mode Transition Coordination

**Consumers:** transition UX / user-decision surface

### Input

```text
transition reference
blocker reference
selected resolution
```

### Result

```text
RESOLUTION_ACCEPTED
BLOCKER_STILL_ACTIVE
INVALID_RESOLUTION
TRANSITION_NO_LONGER_ACTIVE
```

---

## IF-TRANS-003 — Read Transition Status

**Category:** Query

### Output

```text
transition state
source
target
active blockers
commit reached?
terminal outcome if any
```

Consumers use this for presentation/recovery awareness, not to write transition state.

---

## IF-TRANS-004 — Transition Completed

**Category:** Event

### Outcome

```text
COMPLETED
CANCELLED
ROLLED_BACK
FAILED_WITH_SAFE_FALLBACK
```

`COMPLETED` allows Mode State owner to commit target mode only when corresponding commit criteria are satisfied.

---

# 4. Mode policy and application lifecycle

## IF-POLICY-001 — Resolve Effective Mode Policy

**Category:** Query

**Provider:** Mode Policy

**Consumers:** Transition Coordination, Application Lifecycle, Display/Audio/Input/Power contexts

### Input

```text
target/current mode
installation configuration
user preferences
runtime-access context
```

### Output

Semantic desired policy bundle/reference for the requested context.

### Rule

Mode Policy provides desired state, not evidence that Windows has applied it.

---

## IF-APP-001 — Resolve Application Classification

**Category:** Query

**Provider:** Application Lifecycle Policy

### Input

```text
Application identity / evidence
```

### Output

```text
WORK
GAME
GAME_CLIENT
SHARED
SYSTEM
UNKNOWN
```

`UNKNOWN` must not be silently coerced into a class with destructive lifecycle semantics.

---

## IF-APP-002 — Resolve Application Lifecycle Intent

**Category:** Query

**Provider:** Application Lifecycle Policy

**Consumers:** Mode Transition Coordination, Game Launch, Shared App Experience

### Input

```text
Application
classification
source context
target context
```

### Output

Conceptually:

```text
KEEP_RUNNING
START_IF_REQUIRED
STOP_IF_SAFE
REQUIRE_CONFIRMATION
DO_NOT_MANAGE
PRESENT_AS_SHARED_APP
```

### Important

Application internal business/data state remains application-owned.

---

## IF-APP-003 — Report Application Actual State

**Category:** Evidence

**Provider:** runtime/platform observation boundary

**Consumers:** Application Lifecycle Policy, Transition Coordination

### Evidence

```text
process/window/service presence
relevant integration-specific safe-state evidence if supported
observed at
```

Running process presence alone does not prove document-save state.

---

# 5. System context contracts

## IF-DISPLAY-001 — Resolve Desired Display Context

**Provider:** Display Context

**Consumers:** Mode Transition, Game Profiles, Game Launch

### Input

```text
mode
GameProfile if applicable
user preference
HardwareSnapshot
```

### Output

Desired semantic context:

```text
target display
resolution intent
refresh intent
HDR/VRR intent where supported
fallback policy
```

---

## IF-DISPLAY-002 — Apply Display Intent

**Category:** Command

**Provider:** platform-operation adapter boundary

**Consumer:** Display Context / transition or launch orchestration

### Result

```text
OPERATION_SUBMITTED
UNSUPPORTED
DEPENDENCY_UNAVAILABLE
OPERATION_FAILED
```

`OPERATION_SUBMITTED` does not equal target reached.

---

## IF-DISPLAY-003 — Read Display Actual State

**Category:** Evidence

**Provider:** Windows/driver evidence adapter

**Output:** current observed display state + timestamp/freshness.

Display Context compares this with desired context.

---

Audio, Input and Power/Resource contracts follow the same semantic pattern:

```text
Resolve desired state
→ Apply intent through platform boundary
→ Read actual evidence
→ Verify
```

with IDs:

```text
IF-AUDIO-001..003
IF-INPUT-001..002
IF-POWER-001..003
```

---

# 6. Hardware context

## IF-HW-001 — Refresh Hardware Context

**Category:** Query/evidence refresh

**Provider:** Hardware Context Evaluation

**Consumers:** Game Launcher startup, Game Launch Orchestration, Game Profiles, Optimization Policy

### Input

```text
refresh reason
previous snapshot reference if any
```

### Output

```text
HardwareSnapshot
freshness metadata
meaningful-change indication
```

### Rule

Hardware Snapshot is SplitOS-owned interpretation of external evidence, not physical hardware authority.

---

## IF-HW-002 — Hardware Context Invalidated

**Category:** Event

Triggered when evidence indicates that current snapshot/profile assumptions may no longer be valid.

Consumers should request refresh/re-evaluation rather than editing snapshot themselves.

---

# 7. Game library and profiles

## IF-LIB-001 — Reconcile Game Library

**Category:** Reconciliation command

**Provider:** Game Library Representation

**Consumers:** Game Mode UX, startup/library refresh flow

### Input

External Game Client evidence through adapter interfaces.

### Result

```text
RECONCILED
PARTIAL
DEPENDENCY_UNAVAILABLE
STALE_EVIDENCE_RETAINED
```

### Rule

Reconciliation updates SplitOS projection; it does not change external installation/license truth.

---

## IF-LIB-002 — Read Unified Game Library

**Category:** Query

**Provider:** Game Library Representation

### Output

Unified SplitOS representation including projection freshness/launch eligibility where known.

---

## IF-PROFILE-001 — Resolve Effective Game Profile

**Category:** Query / decision

**Provider:** Game Profiles

**Consumers:** Game Launch Orchestration, Game Mode UX, Optimization Policy

### Input

```text
Game
current display/hardware/input context
user-selected profile if any
compatibility knowledge
```

### Output

```text
PROFILE_RESOLVED
PROFILE_REEVALUATION_REQUIRED
NO_VALID_PROFILE
```

with effective `GameProfile` reference/content.

### Rule

Profile resolution cannot rewrite physical hardware capability.

---

## IF-PROFILE-002 — Change Game Profile / Override

**Category:** Command

**Provider:** Game Profiles

**Consumers:** SplitOS Manager, Game Mode UX

Input may express user preference/override. Provider validates and stores canonical SplitOS profile state.

---

## IF-OPT-001 — Resolve Optimization Recommendation

**Category:** Query/decision

**Provider:** Game Optimization Policy

### Input

```text
Game
GameProfile
HardwareSnapshot
Display context
compatibility/game knowledge
```

### Output

Recommendation with constraints and confidence/evidence notes where applicable.

Explicit user override remains higher authority where product policy permits.

---

# 8. Managed game launch

## IF-LAUNCH-001 — Launch Managed Game

**Category:** Transaction command

**Provider:** Game Launch Orchestration

**Consumers:** Game Mode UX, direct managed launch flow

### Preconditions

```text
Managed Runtime Access = ENABLED
Committed mode = GAME
Game launch eligibility acceptable
Effective GameProfile resolved
Required context sufficiently prepared
```

### Input

```text
Game
GameProfile
launch initiator
```

### Immediate result

```text
LAUNCH_ACCEPTED
INVALID_STATE
NOT_ENTITLED
GAME_NOT_AVAILABLE
PROFILE_INVALID
DEPENDENCY_UNAVAILABLE
CONFLICT
```

### Important

`LAUNCH_ACCEPTED` не означает `GAME_RUNNING`.

---

## IF-LAUNCH-002 — Read Game Launch / Session Status

**Category:** Query

### Output

```text
LAUNCHER
PREPARING
CLIENT_HANDOFF
GAME_STARTING
GAME_RUNNING
GAME_EXIT_DETECTED
RETURNING_TO_LAUNCHER
```

plus semantic launch outcome when relevant.

Game Mode UX consumes this state; it does not own it.

---

# 9. Shared Apps

## IF-SHARED-001 — Resolve Shared App Game Presentation

**Provider:** Shared App Experience

**Consumers:** Game Mode UX / runtime window-management integration

### Input

```text
Shared Application
Game Mode policy
user configuration
current display context
active shared-app count
```

### Output

```text
OVERLAY
LOCKED_WINDOW
SECONDARY_DISPLAY
BACKGROUND
NOT_AVAILABLE
```

Constraint for current concept:

```text
max 3 active Shared Apps
```

---

# 10. Compatibility / Update / Recovery

## IF-COMPAT-001 — Resolve Compatibility Decision

**Provider:** Compatibility Management

**Consumers:** Distribution Engineering, Update Orchestration, Game Client adapters, runtime feature decisions

### Input

A concrete version/combination to evaluate.

### Output

```text
SUPPORTED
UNSUPPORTED
UNKNOWN
CONDITIONALLY_SUPPORTED
```

Test evidence is input to the decision, not a competing decision source.

---

## IF-UPDATE-001 — Evaluate Update Eligibility

Combines separately owned facts without merging ownership:

```text
Entitled?
→ Product Identity & Entitlement

Compatible?
→ Compatibility Management

Can update be offered/applied now?
→ Update Orchestration
```

---

## IF-UPDATE-002 — Start Update

**Category:** Transaction command

Immediate result distinguishes accepted/rejected from final outcome.

---

## IF-RECOVERY-001 — Request Recovery

**Category:** Transaction command

**Provider:** Recovery Coordination

Input is failure/context reference, not a consumer-selected arbitrary canonical target.

---

## IF-RECOVERY-002 — Resolve Recovery Target

**Provider:** Recovery Coordination

Consumes:

```text
last committed mode
last-known-good baseline/release
transition/update records
failure evidence
```

Priority remains:

```text
User data integrity
↓
System bootability
↓
Known safe state
↓
Profile restoration
↓
UX restoration
```

---

# 11. Observability

## IF-OBS-001 — Publish Diagnostic Evidence

All responsibility zones may publish diagnostic events to Observability & Diagnostics.

### Critical rule

```text
Diagnostic event
!= canonical operational state
```

Consumers must query the owning interface for authoritative current state.

---

# 12. Result

The internal contract model enforces a central architectural property:

```text
Consumer
→ request/query/event contract
→ canonical owner
→ validated semantic result
```

rather than:

```text
consumer reaches into another zone's storage/state
```

This preserves the ownership model when future components/services are chosen.
