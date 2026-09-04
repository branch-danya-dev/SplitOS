# SPEC-01 — Runtime Module Catalog

## 1. Purpose

This document defines the normative in-process module decomposition of `SplitOS.RuntimeHost.exe`.

The purpose is to prevent the physical process from collapsing the earlier A&D ownership model into one giant manager class.

---

## 2. Core rule

```text
one process
!= one semantic owner
```

A module may consume another module's state only through its public semantic contract.

A module may not silently write another module's canonical data store.

---

## 3. Module catalog

| Module | Canonical responsibility | Owns / decides | Main consumers |
|---|---|---|---|
| `SessionContext` | Windows session context | current session evidence/properties used by runtime | RuntimeAccess, BrokerClient, UI Gateway |
| `RuntimeAccess` | FREE/PRO managed-runtime decision | `ManagedRuntimeAccessDecision` | Mode, Launcher, Manager |
| `ModeState` | committed operational mode | `OperationalModeState` | ModeTransition, GameLaunch, UI |
| `ModeTransition` | Work↔Game transaction lifecycle | active transition attempt/result | ModeState, Policy, AppLifecycle, Recovery |
| `ModePolicy` | target Work/Game policy | effective semantic target policy | transition/system-context modules |
| `ApplicationLifecycle` | app classification/lifecycle policy | lifecycle decisions for managed apps | ModeTransition, SharedApps |
| `DisplayContext` | desired/actual display context semantics | desired display target + verified result interpretation | ModeTransition, Profiles |
| `AudioContext` | desired/actual audio context semantics | desired audio target + evidence interpretation | ModeTransition, Profiles |
| `InputContext` | input/controller context | desired input context + evidence interpretation | GameLauncher, Profiles |
| `PowerContext` | power policy context | desired power context + verified result interpretation | ModeTransition |
| `HardwareContext` | hardware snapshot/evaluation | interpreted hardware snapshot | Profiles, Optimization, GameLaunch |
| `GameLibrary` | unified SplitOS library projection | normalized game/client projection | GameLauncher, GameLaunch |
| `GameProfiles` | game profile ownership | canonical user Game Profiles | GameLauncher, Optimization, GameLaunch |
| `Optimization` | optimization recommendation/effective policy | recommendation result, not user override authority | Profiles, GameLaunch |
| `GameLaunch` | managed game launch transaction | launch attempt/result | GameSession, GameLauncher |
| `GameSession` | managed game runtime lifecycle | canonical managed Game Session state | GameLauncher, Game→Work |
| `SharedApps` | Shared App Game UX semantics | active presentation assignment | GameLauncher, ApplicationLifecycle |
| `Compatibility` | compatibility decision | release/client/config compatibility result | Update, Profiles, RuntimeAccess |
| `UpdateOrchestration` | update transaction orchestration | update operation lifecycle | Recovery, Manager |
| `RecoveryCoordination` | recovery decision and execution coordination | recovery target/result | Startup, Update, ModeTransition |
| `Observability` | diagnostic evidence | diagnostic records only | all modules/support |
| `AdapterRegistry` | integration adapter discovery | adapter/capability registry, not external truth | system/game modules |
| `PersistenceGateway` | physical store abstraction | no semantic ownership; persists owner-approved values | state-owning modules |
| `BrokerClient` | privileged IPC client | no semantic ownership | modules requiring privileged capability |
| `UiGateway` | Manager/Launcher IPC boundary | no canonical state | Manager, GameLauncher |

---

## 4. Module classes

### 4.1 Semantic owners

These modules are allowed to commit canonical SplitOS meaning in their domain:

```text
RuntimeAccess
ModeState
ModeTransition
ModePolicy
ApplicationLifecycle
GameProfiles
GameLaunch
GameSession
Compatibility
UpdateOrchestration
RecoveryCoordination
```

They still MUST verify required external/actual evidence before semantic commit.

### 4.2 Context/evidence interpreters

```text
SessionContext
DisplayContext
AudioContext
InputContext
PowerContext
HardwareContext
GameLibrary
```

They transform external/platform evidence into bounded SplitOS representations.

They MUST preserve source/freshness/provenance metadata where relevant.

### 4.3 Infrastructure modules

```text
Observability
AdapterRegistry
PersistenceGateway
BrokerClient
UiGateway
```

Infrastructure modules MUST NOT become hidden owners simply because all modules use them.

---

## 5. Required semantic contracts

The exact implementation language is not fixed, but the following conceptual contracts MUST exist.

### Session Context

```text
QueryCurrentSession()
IsPhysicalConsoleOwner()
SessionOwnershipChanged
```

### Runtime Access

```text
EvaluateRuntimeAccess()
QueryRuntimeAccess()
RuntimeAccessChanged
```

### Mode State

```text
QueryCommittedMode()
CommitMode(verifiedTransitionResult)
CommittedModeChanged
```

Only `ModeState` may commit the canonical operational mode.

### Mode Transition

```text
RequestTransition(targetMode, correlationId)
CancelTransition(transitionId)
QueryTransition(transitionId)
TransitionStateChanged
```

### Mode Policy

```text
ResolveTargetPolicy(targetMode, context)
```

### Application Lifecycle

```text
InspectApplications(context)
ResolveApplicationActions(targetMode, evidence)
ApplyManagedLifecycleDecision(...)
```

### System context modules

Common semantic shape:

```text
ResolveDesiredContext(...)
ReadActualContext()
ApplyContextIntent(...)
VerifyContext(desired, actual)
ContextEvidenceChanged
```

Not every context applies directly; some use Broker/Windows adapters.

### Game Library

```text
RefreshLibraryProjection()
QueryGame(gameId)
QueryLibrary()
LibraryProjectionChanged
```

### Game Profiles

```text
ResolveProfile(gameId, hardwareContext, userSelection)
SaveUserProfile(...)
QueryProfiles(gameId)
```

### Game Launch

```text
RequestManagedLaunch(gameId, profileId, correlationId)
CancelLaunch(launchId)
QueryLaunch(launchId)
LaunchStateChanged
```

### Game Session

```text
StartSessionFromVerifiedLaunch(...)
RecordGameRunningEvidence(...)
RecordGameExitEvidence(...)
QueryGameSession()
GameSessionChanged
```

### Update / Recovery

```text
RequestUpdate(...)
QueryUpdate(...)
RequestRecovery(...)
QueryRecovery(...)
```

Exact transaction payloads are defined later.

---

## 6. State-write rules

### 6.1 No shared mutable god-state

The implementation MUST NOT use a single mutable object such as:

```text
GlobalRuntimeState {
    mode
    transition
    game
    update
    account
    display
    ...
}
```

as an unrestricted writable state bag.

A read-optimized aggregate/snapshot MAY exist for UI/query purposes, but its fields remain owned by the source modules.

### 6.2 Persistence does not own state

Bad:

```text
ModeTransition → UPDATE state_table SET currentMode='GAME'
```

Required:

```text
ModeTransition
→ verified completion
→ ModeState.CommitMode(GAME)
→ ModeState persists through PersistenceGateway
```

### 6.3 Diagnostics do not repair state

`Observability` may report mismatch but MUST NOT write canonical state as a repair mechanism.

---

## 7. In-process commands, queries and events

### Command

A command asks one owner to perform a semantic operation.

Properties:

- one intended owner;
- contains `operationId`/`correlationId` where applicable;
- returns accepted/rejected/terminal semantic result or operation handle.

### Query

A query reads owner-approved current knowledge.

Queries MUST NOT have mutation side effects other than cache refresh explicitly documented by the owning module.

### Event

An event states that something already happened.

Events MUST NOT be interpreted as permission to bypass the target module's guards.

Example:

```text
DisplayEvidenceChanged
→ HardwareContext invalidates snapshot
→ GameProfiles may re-resolve recommendation
```

not:

```text
DisplayEvidenceChanged
→ directly overwrite active GameProfile
```

---

## 8. Cross-module ordering ownership

No generic event bus may decide business order.

For complex operations, orchestration belongs to the scenario owner.

Examples:

```text
Work→Game ordering
→ ModeTransition

Managed game launch ordering
→ GameLaunch

Update ordering
→ UpdateOrchestration

Recovery ordering
→ RecoveryCoordination
```

A shared dispatcher MAY transport commands/events but cannot become the scenario owner.

---

## 9. Major mutation coordination

The following semantic operations compete for machine-wide mutation authority:

```text
ModeTransition
UpdateOrchestration
RecoveryCoordination
```

They MUST consume one shared in-process contract:

```text
MachineMutationGate
```

Required semantic behavior:

```text
Acquire(operationType, operationId)
→ GRANTED | BUSY | RECOVERY_REQUIRED

Release(operationId)
```

Exact queuing/preemption/durability rules are defined in `SPEC-05` and `SPEC-11`.

The gate MUST NOT itself decide target mode/update/recovery semantics.

---

## 10. Adapter ownership

Adapter modules are mechanism boundaries.

Examples:

```text
WindowsDisplayAdapter
WindowsAudioAdapter
WindowsPowerAdapter
WindowsProcessAdapter
SteamAdapter
EpicAdapter
BrokerClient
AccountBackendClient
```

Adapters MAY return technical results/evidence.

Adapters MUST NOT convert a technical result directly into canonical business state.

Example:

```text
SteamAdapter: HANDOFF_ACCEPTED
```

is consumed by `GameLaunch`, which still waits for actual start evidence before `GameSession=GAME_RUNNING`.

---

## 11. UI projection

`UiGateway` SHOULD expose a read model composed from owner queries.

Example:

```text
RuntimeUiSnapshot
├── runtimeAccess
├── committedMode
├── transitionSummary
├── gameSessionSummary
├── accountSummary
├── deviceSummary
└── health/degradedCapabilities
```

This snapshot is derived presentation data.

It MUST NOT become the source of truth for any field.

---

## 12. Module startup dependency tiers

### Tier 0 — infrastructure

```text
Observability
SessionContext
PersistenceGateway
```

### Tier 1 — safety/control

```text
BrokerClient
RecoveryCoordination
ModeState
RuntimeAccess
```

### Tier 2 — runtime policy

```text
ModePolicy
ApplicationLifecycle
Display/Audio/Input/Power/Hardware
Compatibility
```

### Tier 3 — game experience

```text
GameLibrary
GameProfiles
Optimization
GameLaunch
GameSession
SharedApps
```

### Tier 4 — UX exposure

```text
UiGateway
Manager activation
GameLauncher activation
```

A lower tier failure MAY force higher tiers into degraded/unavailable state.

A higher tier failure MUST NOT automatically corrupt lower-tier canonical state.

---

## 13. Module failure containment

A module failure SHOULD be classified as one of:

```text
OPTIONAL_UNAVAILABLE
CAPABILITY_DEGRADED
OWNER_UNAVAILABLE
RECOVERY_REQUIRED
PROCESS_FATAL
```

Examples:

- Epic adapter failed → `OPTIONAL_UNAVAILABLE` if Steam still works;
- Broker client unavailable → `CAPABILITY_DEGRADED`;
- ModeState durable store unreadable → `RECOVERY_REQUIRED`;
- composition/invariant violation → `PROCESS_FATAL` and safe restart/reconciliation.

---

## 14. Acceptance criteria

Implementation conforms only if:

- semantic owners remain individually addressable in Runtime Host;
- UI cannot write owner state directly;
- PersistenceGateway has no business decision authority;
- adapters return evidence/technical outcomes, not canonical commits;
- Work→Game, game launch, update and recovery orchestration remain separate;
- machine mutation arbitration is a shared gate, not a giant controller;
- an aggregate UI snapshot is derived only;
- one module failure cannot silently overwrite unrelated owner state.
