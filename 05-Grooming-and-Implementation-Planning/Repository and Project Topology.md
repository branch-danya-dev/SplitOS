# Repository and Project Topology

## 1. Purpose

Provides a delivery-oriented source/project topology that preserves the already specified SplitOS process, ownership, trust and release boundaries.

This is an implementation planning baseline, not a new architecture authority.

---

## 2. Repository strategy

### Proposed v1 baseline

Use one product source repository / monorepo during the first implementation program, with strongly separated projects/packages.

Reasoning:

```text
Runtime + Broker + Manager + Launcher
+ shared contracts
+ Builder/update/recovery tooling
+ verification fixtures
```

must evolve under tightly coordinated protocol/release compatibility.

A monorepo allows one atomic change to carry:

- protocol/schema updates;
- executable changes;
- test fixtures;
- release manifest changes;
- migration changes;
- acceptance evidence definitions.

This is not a requirement that backend deployment or signing infrastructure share the same runtime process or security boundary.

### Split trigger

A future repository split is reasonable only when there is concrete delivery/ownership value, for example:

- independent backend release cadence;
- separate infrastructure access controls;
- signing/release tooling custody requirements;
- independent team scaling;
- compliance boundary.

Repository separation must not redefine product ownership semantics.

---

## 3. Technology-neutral source tree

Until implementation language/UI framework decisions are explicitly closed, use semantic project names rather than framework-specific names.

```text
/
├── src/
│   ├── Desktop/
│   │   ├── RuntimeHost/
│   │   ├── Manager/
│   │   └── GameLauncher/
│   │
│   ├── Machine/
│   │   ├── BrokerService/
│   │   ├── UpdateBootstrap/
│   │   └── RecoveryTool/
│   │
│   ├── Core/
│   │   ├── Contracts/
│   │   ├── Domain/
│   │   ├── ModeRuntime/
│   │   ├── GameRuntime/
│   │   ├── Profiles/
│   │   ├── Compatibility/
│   │   └── Diagnostics/
│   │
│   ├── Persistence/
│   │   ├── MachineStore/
│   │   ├── UserStore/
│   │   ├── ProjectionStore/
│   │   ├── Migrations/
│   │   └── ProtectedSecrets/
│   │
│   ├── Windows/
│   │   ├── Display/
│   │   ├── Audio/
│   │   ├── Input/
│   │   ├── Power/
│   │   ├── Process/
│   │   ├── Hardware/
│   │   └── Services/
│   │
│   ├── Adapters/
│   │   ├── GameClients/
│   │   │   ├── Steam/
│   │   │   ├── Epic/
│   │   │   ├── MicrosoftGaming/
│   │   │   └── BattleNet/
│   │   ├── GameConfig/
│   │   └── PerformanceTelemetry/
│   │
│   ├── Builder/
│   │   ├── SourceIdentity/
│   │   ├── Manifest/
│   │   ├── Servicing/
│   │   ├── ComponentMatrix/
│   │   └── MediaAssembly/
│   │
│   ├── UpdateRecovery/
│   │   ├── UpdateClient/
│   │   ├── ReleaseVerification/
│   │   ├── RecoveryCapsule/
│   │   └── RecoveryCoordination/
│   │
│   └── Backend/
│       ├── Account/
│       ├── Entitlement/
│       └── ReleaseKnowledge/
│
├── contracts/
│   ├── ipc/
│   ├── backend/
│   ├── manifests/
│   ├── diagnostics/
│   └── fixtures/
│
├── knowledge/
│   ├── components/
│   ├── games/
│   ├── clients/
│   ├── compatibility/
│   └── recovery/
│
├── tests/
│   ├── Unit/
│   ├── Component/
│   ├── IPC/
│   ├── WindowsIntegration/
│   ├── EndToEnd/
│   ├── FaultInjection/
│   ├── Security/
│   ├── Performance/
│   └── Fixtures/
│
├── packaging/
│   ├── Runtime/
│   ├── Service/
│   ├── SetupProvisioning/
│   ├── WinRE/
│   └── Release/
│
├── eng/
│   ├── scripts/
│   ├── local-dev/
│   ├── test-images/
│   └── lab/
│
└── docs/
    └── implementation/
```

The exact physical path names may change with the chosen build ecosystem. The dependency rules below are the important part.

---

## 4. Executable/project mapping

| Release artifact | Source boundary | Runs as | Primary responsibility |
|---|---|---|---|
| `SplitOS.RuntimeHost.exe` | `src/Desktop/RuntimeHost` | current Windows user | user-session orchestration |
| `SplitOS.Manager.exe` | `src/Desktop/Manager` | current Windows user | control center / First Run / account UX |
| `SplitOS.GameLauncher.exe` | `src/Desktop/GameLauncher` | current Windows user | controller-first GAME UX |
| `SplitOS.Broker.Service.exe` | `src/Machine/BrokerService` | Windows Service | bounded privileged machine mutations |
| `SplitOS.UpdateBootstrap.exe` | `src/Machine/UpdateBootstrap` | trusted maintenance context | one-shot verified release activation |
| SplitOS Recovery Tool | `src/Machine/RecoveryTool` | WinRE/recovery context | bounded repair/restore |
| SplitOS Media Builder | `src/Builder/*` | build workstation | validated prepared Windows baseline |
| Account/Entitlement service | `src/Backend/*` | backend | SplitOS account/entitlement authority |

---

## 5. Dependency direction rules

### 5.1 UI

```text
Manager
GameLauncher
    ↓
UI-facing contracts / Runtime IPC client
    ↓
Runtime Host
```

UI projects MUST NOT reference:

- Broker implementation;
- machine persistence implementation;
- service-control wrappers;
- privileged Windows mutation libraries.

### 5.2 Runtime Host

```text
RuntimeHost
→ domain/owner modules
→ adapters / persistence gateways / Broker client
```

Runtime Host coordinates semantic owners but should not become one giant module that bypasses them.

### 5.3 Broker

```text
BrokerService
→ Broker contracts
→ machine persistence
→ allowlisted privileged Windows mechanisms
```

Broker MUST NOT depend on:

- Manager/Game Launcher;
- account UI;
- arbitrary plugin code supplied by user data;
- game/client metadata as executable instructions.

### 5.4 Core contracts

`Core/Contracts` is for intentionally stable DTO/envelope/schema definitions.

It MUST NOT become a generic helper dumping ground.

Forbidden pattern:

```text
Core.Shared.AdminUtils.RunAnything(...)
```

### 5.5 Windows adapters

Windows adapters expose typed state/evidence operations such as:

```text
ReadDisplaySnapshot()
ApplyDisplayTarget(target)
ReadActivePowerScheme()
ApplyManagedServicePolicy(id)
```

They do not decide semantic mode commits.

### 5.6 Game Client adapters

Game Client adapters are read/evidence/handoff integrations.

They do not depend on privileged Broker execution.

### 5.7 Backend

Backend projects can share versioned contract schemas, but MUST NOT depend on desktop implementation assemblies/packages merely for convenience.

---

## 6. Dependency layering model

A practical layering baseline:

```text
Presentation
    ↓
Application / Orchestration
    ↓
Domain / Semantic Owners
    ↓
Ports / Contracts
    ↓
Infrastructure Adapters
```

For privileged operations:

```text
Runtime semantic request
↓
Broker client contract
↓
Named Pipe
↓
Broker capability handler
↓
Windows mechanism
```

For persistence:

```text
Semantic owner
↓
repository/gateway contract
↓
store implementation
↓
SQLite / DPAPI / filesystem
```

---

## 7. Projects that should remain independently testable

At minimum:

- Mode State / Transition;
- Runtime Access / entitlement evaluator;
- mutation lease/fencing;
- profile selector;
- optimizer;
- Game Session correlation;
- each Windows adapter;
- each Game Client adapter;
- Build Manifest validator/executor;
- Component Matrix resolver;
- release/TUF verification;
- RecoveryAuthorization evaluator;
- diagnostic redaction/export.

These can be physically packaged together while still retaining independent tests.

---

## 8. Contract artifacts should be first-class files

Do not hide protocol/schema definitions only inside implementation classes.

Recommended first-class contract directories:

```text
contracts/ipc/
→ Runtime UI protocol
→ Runtime Broker protocol

contracts/backend/
→ account/auth/entitlement API schemas

contracts/manifests/
→ BuildManifest
→ Release Envelope
→ RecoveryAuthorization
→ Component Matrix

contracts/diagnostics/
→ event envelope
→ bundle manifest
```

Benefits:

- easier compatibility testing;
- schema fixture testing;
- change review;
- cross-language implementation if ever needed;
- release artifact binding.

---

## 9. Release knowledge placement

Release-owned knowledge is not user mutable configuration.

```text
knowledge/components
→ Windows Component Matrix / service/policy IDs

knowledge/games
→ optimization setting definitions / config adapters

knowledge/clients
→ supported client capability rules

knowledge/compatibility
→ Windows/driver/client support decisions

knowledge/recovery
→ authorized recovery edges / compatibility metadata
```

Production packaging/signing determines which of these become trusted release targets.

---

## 10. Test topology

### Fast tests

```text
tests/Unit
tests/Component
tests/IPC
```

Run without destructive Windows mutation where possible.

### Windows integration tests

```text
tests/WindowsIntegration
```

Need disposable supported Windows test environments.

### End-to-end tests

```text
tests/EndToEnd
```

Cover installed Runtime/Manager/Launcher/Broker verticals.

### Fault / security / performance

Dedicated because their orchestration/evidence differs:

```text
tests/FaultInjection
tests/Security
tests/Performance
```

These map directly to SPEC-14 gate families.

---

## 11. Build-system expectations independent of chosen stack

The chosen implementation ecosystem must support:

- reproducible/identifiable build outputs;
- separate user and service executables;
- Windows Service packaging;
- Authenticode signing integration;
- schema generation/validation;
- deterministic tests;
- Windows native API interop;
- WinRE-compatible recovery packaging where applicable;
- code coverage/test reports;
- immutable release artifact hashes.

The implementation-language decision is blocked until candidate stacks are evaluated against these needs.

---

## 12. Proposed monorepo branch/release discipline

Implementation PRs should identify:

```text
Affected SPEC IDs
Affected semantic owners
Affected contracts
Migration required? yes/no
Security boundary changed? yes/no
Verification cases added/changed
```

A contract-breaking change must update compatibility fixtures in the same change or be explicitly blocked.

---

## 13. Topology conclusion

The implementation should optimize for one principle:

```text
physical co-location may reduce operational complexity
but must not erase ownership, trust or contract boundaries
```

The initial monorepo structure is therefore a delivery convenience, not a reason to weaken the architecture.