# SplitOS — Deployment and Process Topology

## 1. Purpose

Документ фиксирует, где логические части SplitOS должны исполняться и какие trust/process boundaries между ними обязательны.

Это synthesis-level deployment view, а не финальный installer/service specification.

---

## 2. Deployment domains

```text
Developer / Release Environment
        ↓
User Build Environment
        ↓
Prepared Installation Media
        ↓
Installed Windows Machine
        ├── Interactive User Session
        ├── Privileged Service Domain
        └── Local Persistent State

Remote
├── SplitOS Backend
├── Payment Provider
├── Release/Artifact Distribution
└── External Game/Platform Services
```

---

## 3. Build-time topology

### User Build Environment

```text
SplitOS Media Builder
├── Windows Source Validator
├── Build Manifest Executor
├── SplitOS Package Set
├── Component Classification knowledge
└── Baseline Verifier
        ↓
Supported servicing mechanisms
        ↓
Prepared baseline/media
```

Build-time actions should occur outside the final installed runtime semantics.

The Builder does not need SplitOS PRO entitlement to define whether Windows may be prepared unless monetization policy explicitly says otherwise; exact commercial distribution rules remain product policy.

---

## 4. Installed machine topology

### 4.1 Interactive user session

Expected interactive processes/surfaces:

```text
SplitOS.Manager.exe
SplitOS.GameLauncher.exe
SplitOS.RuntimeHost.exe
External Game Clients
Games
Normal Windows applications
```

All interactive UX belongs here.

The Runtime Host should run in the context of the currently signed-in Windows user/session because it needs user-session state, process/window/device context and per-user SplitOS identity/configuration.

---

### 4.2 Privileged service domain

Expected privileged process:

```text
SplitOS.Broker.Service.exe
```

Properties:

- Windows Service / non-interactive privileged context;
- no primary user-facing UI;
- no direct Game Launcher UX ownership;
- only bounded allowlisted capability surface;
- explicit IPC security;
- separate lifecycle from Runtime Host.

This separation prevents the entire interactive SplitOS stack from requiring administrator/SYSTEM privilege.

---

### 4.3 Local persistence domain

Conceptual locations:

```text
Per-user SplitOS state
Machine-level SplitOS state
Protected secret material
Transaction/recovery journal
Diagnostic data
Installed baseline metadata
```

Per-user and machine-wide data must remain separated according to ownership and security semantics.

Example logical split:

```text
User-scoped
├── account association metadata
├── user settings/preferences
├── Game Profiles
├── protected user tokens
└── per-user projection caches

Machine-scoped
├── InstalledBaselineIdentity
├── release/runtime version metadata
├── machine update/recovery journal
└── machine-level component state metadata
```

Exact filesystem/registry/database placement remains OPEN.

---

## 5. User/session cardinality

Architecture must not assume one Windows user forever.

Conceptually:

```text
Machine
├── Windows User A
│   └── SplitOS Account A / entitlement A
└── Windows User B
    └── SplitOS Account B / entitlement B
```

Therefore user-scoped runtime state must be resolved per active Windows session.

Open implementation questions include simultaneous multi-session support and Fast User Switching behavior.

v1 may explicitly constrain supported session concurrency, but the data/trust model must not silently merge identities.

---

## 6. Process ownership

### Manager

```text
Process privilege: normal interactive user
Primary concerns: UX/configuration/account management
Canonical writes: none directly; requests through semantic contracts
```

### Game Launcher

```text
Process privilege: normal interactive user
Primary concerns: Game Mode UX/game launch requests
Canonical writes: none directly
```

### Runtime Host

```text
Process privilege: normal interactive user
Primary concerns: semantic runtime orchestration
Can mutate user-session state through supported APIs
Privileged machine mutation: via Broker only where required
```

### Privileged Broker

```text
Process privilege: elevated/service identity
Primary concern: execute bounded machine-level capabilities
Canonical semantic decisions: no
```

### External Game Clients/Games

```text
External processes
Authority: own auth/license/install/runtime facts where applicable
No direct privileged SplitOS authority
```

---

## 7. Startup topology

### Windows boot

```text
Windows boot
↓
Broker Service starts according to service policy
↓
Windows sign-in
↓
Runtime Host starts for active user/session
↓
resolve local state/account/entitlement
↓
Manager First Run or normal runtime path
```

The Broker may exist before a user is signed in, but should not invent user-specific mode decisions without an authorized interactive runtime context.

### FREE

```text
Runtime Host
→ ManagedRuntimeAccess = DISABLED
→ normal Windows Desktop
→ Manager remains available according to FREE policy
```

Game Launcher need not be activated as primary shell/UX.

### PRO

```text
Runtime Host
→ ManagedRuntimeAccess = ENABLED
→ mode startup policy
→ WORK or GAME
```

If GAME is committed, Game Launcher becomes primary visible gaming UX.

---

## 8. IPC topology

Canonical privileged path:

```text
Manager/Game Launcher
        ↓ semantic request
Runtime Host
        ↓ bounded privileged request
Authenticated + authorized local IPC
        ↓
Privileged Broker
        ↓
Windows privileged operation
        ↓
technical result
        ↑
Runtime Host
        ↓
read actual state + verify
        ↓
semantic commit/result
```

No direct:

```text
Game Launcher → Broker arbitrary admin command
Manager → SCM/registry unrestricted mutation
External client metadata → Broker
```

---

## 9. Backend topology

```text
Runtime Host / Manager
        ↓ HTTPS
SplitOS Backend
├── Account/Auth
├── Entitlement
├── Subscription reconciliation
├── optional offline entitlement issuer
└── possible release metadata surface
        ↓
Payment Provider
```

The desktop client is a public/native client and should not contain reusable backend client secrets.

---

## 10. Release/update topology

```text
Release Authority
↓
Signed release/update metadata
↓
Artifact distribution
↓
Runtime/Updater receives candidate metadata/artifacts
↓
verify signature/digest/version transition
↓
protected local staging
↓
Update Orchestrator
↓
Privileged Broker / servicing mechanism
↓
reboot if required
↓
Runtime Host reconciles transaction
↓
verify actual installed state
↓
commit InstalledBaselineIdentity
```

Transport/download success is not a trust or update-success boundary.

---

## 11. Game launch topology

```text
Game Launcher
↓
Runtime Host / Game Launch Orchestrator
├── Game Library Projection
├── Hardware Context
├── Game Profile
├── Display/Input/Power adapters
└── Game Client Adapter
        ↓
External Game Client
        ↓
Game process
```

The Game Client may open authentication/update UX during handoff; SplitOS must observe rather than bypass external ownership.

---

## 12. Crash/restart topology

### Runtime Host restart

Runtime Host is restartable independently from Windows boot.

On restart:

```text
load durable canonical/transaction state
↓
read actual Windows/external evidence
↓
reconcile
↓
continue stable state or Recovery
```

No assumption that process memory was canonical durability.

### Broker restart

Broker restart does not itself change committed operational mode.

Outstanding privileged operations must be reconciled by transaction/evidence semantics rather than assumed completed.

---

## 13. Session 0 boundary

The Privileged Broker belongs to non-interactive service context.

User-facing dialogs should not be implemented by trying to show UI from the service domain.

If a privileged operation requires user decision:

```text
Broker/operation result
→ Runtime Host
→ Manager/Game Launcher UI
→ user decision
→ new authorized semantic request
```

---

## 14. Deployment invariants

1. User-facing SplitOS UX runs in interactive user context.
2. Privileged machine mutation is isolated from general UI/runtime privilege.
3. Runtime Host is the main semantic orchestration boundary in the user session.
4. Broker executes capabilities but does not decide product truth.
5. Local persisted state survives Runtime Host restart where semantics require durability.
6. Account backend is not required for Windows authentication/bootability.
7. External Game Clients remain independent external processes/authorities.
8. Release signing/provenance authority is separate from ordinary runtime processes.
9. Cross-process communication never removes the need for semantic verification.
10. Physical packaging may evolve without changing logical ownership contracts.

---

## 15. Candidate v1 physical deployment

```text
Installed machine
├── SplitOS.Manager.exe
├── SplitOS.GameLauncher.exe
├── SplitOS.RuntimeHost.exe
├── SplitOS.Broker.Service.exe
├── SplitOS runtime libraries/adapters
├── SplitOS local state
└── SplitOS updater/recovery payload as defined later

Build machine/user environment
└── SplitOS.MediaBuilder.exe

Remote
├── api.splitos-like backend boundary
├── auth/entitlement services
├── release/artifact distribution
└── external payment provider
```

Names/hostnames are illustrative; exact package naming is not canonical yet.

---

## 16. Result

Deployment synthesis establishes the main physical principle:

```text
interactive semantics
        ↓
normal-user Runtime Host
        ↓
only bounded privilege crossing
        ↓
Privileged Broker
```

while backend, Game Clients, Windows platform and release authority remain explicit external/separate trust domains.