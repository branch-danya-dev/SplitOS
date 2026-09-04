# SplitOS — Integration Architecture

## 1. Purpose

Документ определяет **как уже зафиксированные semantic interfaces SplitOS реализуются через конкретные integration mechanisms**.

Цепочка reasoning:

```text
Responsibility
→ Ownership
→ State / Behavior
→ Data
→ Interface semantics
→ Integration mechanism
```

Integration layer отвечает на вопрос:

> Каким реальным механизмом одна зона SplitOS взаимодействует с другой зоной, Windows или внешней системой, не разрушая ранее определённый ownership?

Этот слой **не** фиксирует окончательные class names, process names, deployment package names или complete production architecture. Он фиксирует integration boundaries и выбранные/кандидатные механизмы.

---

## 2. Core integration principles

### INT-RULE-001 — UI does not own privileged system state

SplitOS Manager и Game Launcher не должны напрямую выполнять privileged Windows mutations.

```text
UI
→ semantic request
→ Runtime Host
→ privileged/system integration where needed
→ verification
→ canonical result
```

### INT-RULE-002 — Service must not be interactive UI

Windows services работают вне interactive user desktop. Поэтому SplitOS не должен строить UI внутри privileged service.

Target pattern:

```text
Interactive user session
├── SplitOS Manager
├── Game Launcher
└── SplitOS Runtime Host

Session 0 / service context
└── SplitOS Privileged Broker
```

### INT-RULE-003 — Privilege must be split by operation

Не каждая операция требует elevation.

Examples:

```text
Read user display topology       → user session
Game Client launch               → user session
Read audio endpoints             → user session
User UI                           → user session

Manage protected service state   → privileged broker where required
Protected machine policy         → privileged broker where required
System-level recovery action     → privileged broker where required
```

### INT-RULE-004 — Applied command must be read back

```text
Apply
!=
Verified
```

Для platform operations используется pattern:

```text
Resolve desired state
→ invoke integration mechanism
→ receive immediate call result
→ read actual state
→ compare
→ semantic success/failure
```

### INT-RULE-005 — External adapters normalize semantics, not authority

Steam/Epic/Xbox/Battle.net adapters дают SplitOS унифицированные результаты, но не превращают SplitOS в владельца installation/license truth.

### INT-RULE-006 — Unsupported mechanism remains OPEN

Если публичный/поддерживаемый механизм не подтверждён, Integration layer не должен маскировать reverse-engineered implementation как canonical API.

Statuses:

```text
VERIFIED       official/supported mechanism documented
CANDIDATE      technically reasonable, needs prototype/compatibility validation
BEST_EFFORT    useful but based on version-sensitive/non-contractual evidence
OPEN           mechanism not yet established
REJECTED       mechanism conflicts with safety/ownership/product boundary
```

---

## 3. Target runtime integration topology

```text
┌──────────────────────── Windows user session ────────────────────────┐
│                                                                     │
│  SplitOS Manager          SplitOS Game Launcher                     │
│        │                         │                                   │
│        └──────── semantic local contracts ────────┐                 │
│                                                   ▼                 │
│                                      SplitOS Runtime Host           │
│                                      ├─ Runtime Access              │
│                                      ├─ Mode orchestration          │
│                                      ├─ Game orchestration          │
│                                      ├─ User-session Windows APIs    │
│                                      └─ Game Client adapters         │
│                                                   │                 │
└───────────────────────────────────────────────────┼─────────────────┘
                                                    │ secured IPC
                                                    ▼
                                     ┌─────────────────────────┐
                                     │ SplitOS Privileged      │
                                     │ Broker / Windows Service│
                                     └───────────┬─────────────┘
                                                 │
                                    protected Windows operations

SplitOS Runtime Host
        │ HTTPS
        ▼
SplitOS Account Backend
        │
        ├── entitlement/account data
        └── hosted payment flow integration

Media Builder
        │ offline servicing
        ▼
Microsoft-authorized Windows source
```

Names `Runtime Host` and `Privileged Broker` are analytical component labels. Final executable/service naming is implementation-level.

---

## 4. Local process boundary

### 4.1 SplitOS Runtime Host

**Status:** CANDIDATE → recommended integration baseline.

Runs in the interactive Windows user session and coordinates user-scoped runtime integrations.

Responsibilities consumed from earlier models:

```text
account/session association consumption
runtime-access evaluation orchestration
mode-transition coordination
Game Launcher orchestration
hardware/display/audio/input evidence
Game Client adapters
Shared App presentation coordination
```

It does **not** become the canonical owner of every domain fact. It hosts integration logic that calls the corresponding owners/contracts.

### 4.2 SplitOS Privileged Broker

**Status:** CANDIDATE → recommended integration baseline.

Windows Service used only for operations requiring machine-level privilege or durable service-context execution.

Important constraints:

- no interactive UI;
- no direct ownership of user intent;
- no independent mode decision;
- expose a narrow allowlisted command surface;
- reject arbitrary command execution;
- verify caller/session/account context where relevant;
- use least privilege / service SID / restricted ACL strategy where practical.

### 4.3 UI processes

Manager and Game Launcher remain replaceable consumers.

They may:

```text
query state
send semantic requests
render progress/results
```

They must not:

```text
write canonical mode state directly
edit entitlement directly
invoke arbitrary privileged commands
create competing Game Client truth
```

---

## 5. Local IPC selection

### Selected candidate

```text
Windows Named Pipes
+ explicit ACL
+ authenticated caller/session validation
+ request/response correlation
```

**Status:** CANDIDATE / preferred baseline.

Rationale:

- native Windows IPC;
- suited to desktop process ↔ Windows Service communication;
- supports explicit security descriptors;
- avoids opening a network port for local privileged control;
- fits command/query/event contracts already defined.

### Security requirement

Do not rely on default pipe ACL.

The integration must explicitly restrict clients to expected local identities/session conditions.

### Contract envelope concept

```text
requestId
contractVersion
callerContext
operation
payload
```

Response:

```text
requestId
accepted/rejected
semanticResult
errorCategory
evidence/correlation metadata
```

Exact serialization remains open:

```text
JSON
MessagePack
protobuf
other
```

This choice belongs to implementation/specification after contract stability review.

---

## 6. Integration domains

| Domain | Main mechanism direction | Status |
|---|---|---|
| user/session | Win32/WTS user-session APIs | VERIFIED/CANDIDATE |
| display | CCD APIs (`QueryDisplayConfig`, `SetDisplayConfig`) | VERIFIED |
| audio discovery/events | Windows Core Audio / MMDevice APIs | VERIFIED |
| audio default switching | public supported mechanism not yet established | OPEN |
| power plan | PowrProf APIs | VERIFIED |
| process evidence | Win32 process enumeration + process handles/events where allowed | VERIFIED/CANDIDATE |
| Windows services | Service Control Manager APIs | VERIFIED |
| local UI↔privileged IPC | named pipes with explicit ACL | CANDIDATE |
| Game Clients | per-client adapters | CANDIDATE, mechanism-specific |
| SplitOS account backend | HTTPS application API | CANDIDATE |
| payment | hosted checkout + backend-side provider notification | CANDIDATE |
| Media Builder | DISM/offline servicing + SplitOS manifest executor | VERIFIED/CANDIDATE |
| update lifecycle | signed SplitOS update metadata + controlled Windows package integration | CANDIDATE |

---

## 7. User-session affinity

Some integrations are inherently user-session-bound.

Examples:

```text
Game Launcher
Game Client process
interactive display topology
current user audio environment
Shared App windows
Windows user identity/session context
```

A service in Session 0 must not pretend it has the same interactive desktop context.

Therefore SplitOS should prefer:

```text
user-session integration performed by Runtime Host
```

and use the privileged broker only when required.

---

## 8. Privileged operation categories

Potential broker operation categories:

```text
SERVICE_STATE_APPLY
MACHINE_POLICY_APPLY
SYSTEM_RECOVERY_ACTION
PROTECTED_FILE_OR_REGISTRY_ACTION
UPDATE_APPLY
MODE_MANAGED_MACHINE_COMPONENT_APPLY
```

Every category needs:

```text
allowlisted operation
validated parameters
caller authorization
idempotency expectations
read-back verification path
failure classification
```

Forbidden generic contract:

```text
Execute(commandLine)
```

because it turns IPC into arbitrary privilege escalation surface.

---

## 9. External HTTPS boundary

SplitOS Account/Entitlement backend should use a normal encrypted application protocol boundary.

Baseline direction:

```text
Desktop Runtime / Manager
        ↓ HTTPS
SplitOS Account Backend
```

The backend owns server-side account/entitlement persistence.

The local product owns local runtime-access decision under offline policy.

Exact authentication mechanism remains OPEN pending Trust analysis:

```text
OIDC/OAuth-style browser flow
passkey/password based first-party auth
other
```

Do not freeze credentials/token design before `10-Trust`.

---

## 10. Hosted payment boundary

Preferred direction:

```text
Manager
→ request checkout session from SplitOS backend
→ open hosted checkout in system browser / provider-controlled page
→ provider sends authoritative transaction evidence to backend
→ backend updates entitlement
→ local Runtime refreshes entitlement
```

Desktop callback alone must not be treated as payment authority.

---

## 11. Integration compatibility rule

Every integration adapter must expose compatibility metadata.

Conceptually:

```text
adapter version
supported external version/range
capabilities
known limitations
validation status
```

Compatibility Management remains owner of support decisions.

---

## 12. No universal adapter assumption

SplitOS must not assume that one technical mechanism works for every external Game Client or Windows subsystem.

```text
Semantic interface
      ↓
Integration adapter
      ↓
platform-specific mechanism
```

This permits:

```text
SteamAdapter
EpicAdapter
XboxAdapter
BattleNetAdapter
```

without changing the Game Launch semantic contract.

---

## 13. Source evidence used for integration baseline

Official Windows documentation currently supports the following directions:

- Windows services and Session 0 isolation; UI should communicate indirectly with a service.
- Named pipes support explicit security descriptors/ACLs.
- Windows CCD APIs expose query/apply display topology/mode operations.
- Core Audio MMDevice APIs expose endpoint enumeration/default endpoint reads/device notifications.
- PowrProf exposes active power-scheme read/apply operations.
- DISM supports offline servicing of Windows features/packages/provisioned apps.

Canonical reference URLs:

```text
https://learn.microsoft.com/en-us/windows/win32/services/interactive-services
https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig
https://learn.microsoft.com/en-us/windows/win32/coreaudio/core-audio-interfaces
https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme
https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/enable-or-disable-windows-features-using-dism?view=windows-11
```

These references support mechanism feasibility; they do not replace SplitOS compatibility testing.

---

## 14. Decisions established by this layer

### INT-001
Use separate interactive user-session runtime and non-interactive privileged service boundary.

### INT-002
Do not let UI components directly invoke broad privileged Windows operations.

### INT-003
Use secured local IPC; Windows Named Pipes are the preferred baseline candidate.

### INT-004
Keep display/Game Client/user-session integrations in interactive session unless privilege/technical constraints require otherwise.

### INT-005
Require read-back verification after state-changing Windows integrations.

### INT-006
Use adapter isolation for Game Clients.

### INT-007
Use backend-mediated hosted checkout; local desktop is not payment authority.

### INT-008
Use DISM/offline servicing as the primary supported Windows-image servicing family for Builder operations where applicable.

---

## 15. Open integration decisions

- exact account authentication protocol and token storage;
- final Runtime Host / Broker process packaging model;
- exact IPC serialization/version negotiation;
- documented method for system-wide default audio endpoint switching;
- HDR/VRR operation support matrix by Windows build/driver;
- exact per-client game discovery/launch mechanisms;
- reliable game-process identity mapping for multi-process launchers;
- update package format/signature/install mechanism;
- app-specific safe-save integrations for Work→Game;
- GPU vendor integrations, if needed beyond standard Windows APIs.

---

## 16. Result

Integration baseline:

```text
UI
→ semantic contracts
→ user-session Runtime Host
→ platform adapters
→ secured privileged broker only where required
→ external/platform mechanisms
→ evidence read-back
→ canonical owner verifies result
```

The next `08-Flows` layer should now connect these integration mechanisms into end-to-end execution sequences.