# SPEC-01 — Runtime Process & Module Specification

Status: **READY FOR REVIEW**  
Scope: installed runtime process topology and Runtime Host internal module boundaries.  
Out of scope: physical DB schemas (`SPEC-03`), auth protocol details (`SPEC-04`), exact mode transaction schema (`SPEC-05`), concrete Windows integration algorithms (`SPEC-06`).

---

## 1. Purpose

This specification converts the Synthesis runtime topology into executable/process rules.

It MUST preserve these invariants:

```text
FREE → ManagedRuntime=DISABLED → OperationalMode=NONE
PRO  → ManagedRuntime=ENABLED  → WORK xor GAME
```

and:

```text
UI != canonical state writer
Privileged Broker != semantic owner
technical success != semantic commit
```

---

## 2. Normative process set

The installed SplitOS runtime consists of four process roles.

| Process | Deployment | Privilege | Cardinality | Primary role |
|---|---|---:|---:|---|
| `SplitOS.RuntimeHost.exe` | interactive Windows session | normal user | 1 per eligible session | session orchestration and semantic owners |
| `SplitOS.Manager.exe` | interactive Windows session | normal user | 0..1 per session | account/settings/subscription/control-center UX |
| `SplitOS.GameLauncher.exe` | interactive Windows session | normal user | 0..1 per session | primary Game Mode UX |
| `SplitOSBroker` / `SplitOS.Broker.Service.exe` | Windows Service / Session 0 | LocalSystem v1 baseline | exactly 1 per machine | bounded privileged machine mutations |

### 2.1 Packaging rule

Each process MUST be a separately versioned executable artifact in the same SplitOS release manifest.

A release MUST NOT intentionally deploy arbitrary combinations of incompatible local component major protocol versions.

### 2.2 No elevation for interactive executables

`RuntimeHost`, `Manager`, and `GameLauncher` MUST run as the signed-in Windows user and MUST NOT require permanent elevation.

Privileged actions MUST cross the Broker boundary.

---

## 3. Runtime Host role

`RuntimeHost` is the composition root for interactive-session SplitOS behavior.

It is **not** a new semantic owner replacing the A&D responsibility model.

Internally it MUST preserve distinct modules for:

```text
Session Context
Runtime Access
Mode State
Mode Transition
Mode Policy
Application Lifecycle
Display Context
Audio Context
Input Context
Power Context
Hardware Context
Game Library
Game Profiles
Optimization
Game Launch
Game Session
Shared Apps
Compatibility
Update Orchestration
Recovery Coordination
Observability
Integration Adapters
Persistence Gateways
Broker Client
UI Gateway
```

No module may mutate another module's canonical state by bypassing its public semantic contract.

---

## 4. Session cardinality

### 4.1 Runtime Host

There MUST be at most one `RuntimeHost` instance for a Windows logon session.

Recommended single-instance identity:

```text
Local\SplitOS.RuntimeHost.S<SessionId>
```

An attempted second instance MUST detect the existing instance and terminate without changing state.

### 4.2 Manager

There MUST be at most one Manager window/process instance per Windows session.

A second activation SHOULD forward activation intent to the existing instance through the Runtime Host and then exit.

### 4.3 Game Launcher

There MUST be at most one Game Launcher process per Windows session.

Game Launcher SHOULD exist only when:

- committed mode is `GAME`; or
- a recovery/degraded Game Mode UX explicitly requires it.

Closing Game Launcher by normal UI MUST NOT implicitly commit `WORK`.

---

## 5. Machine control session — v1 decision

Operational mode is machine-wide.

Therefore v1 defines exactly one **Managed Control Session** at a time.

```text
Managed Control Session
= Windows session currently attached to the physical console
```

The current physical console session MUST be resolved using supported Windows session APIs.

### 5.1 Eligible session

A Runtime Host is eligible to request machine-wide managed mutations only when all are true:

1. its process is running inside the current physical console session;
2. its Windows user/session identity is valid;
3. SplitOS runtime access permits the requested capability;
4. no conflicting major mutation owns the machine mutation window;
5. Broker caller validation succeeds.

### 5.2 Secondary sessions

Other logged-on/disconnected/RDP sessions MAY run a Runtime Host for account/settings/read-only behavior, but MUST NOT independently mutate machine-wide Work/Game state in v1.

Such a session receives a semantic outcome such as:

```text
SESSION_NOT_CONTROL_OWNER
```

not an arbitrary permission error.

### 5.3 Fast user switching

When physical console ownership changes:

```text
old console session
→ loses machine-control eligibility
→ no new machine mutation may start

new console session
→ Runtime Host reconciles machine state
→ may become control owner only after reconciliation
```

An in-progress durable Update/Recovery transaction is not transferred by assumption; its owning transaction semantics remain authoritative.

---

## 6. Startup model

### 6.1 Broker

Broker MUST be registered as an automatic Windows service and SHOULD be available before normal user logon.

Broker startup does not enable PRO behavior by itself.

### 6.2 Runtime Host

Runtime Host MUST start after interactive Windows sign-in in the user's security context.

v1 baseline mechanism:

```text
Windows Task Scheduler
→ Logon trigger
→ interactive token
→ SplitOS.RuntimeHost.exe
```

The task MUST NOT request elevated/highest execution merely to start Runtime Host.

### 6.3 Manager first-run activation

If Runtime Host resolves that the current Windows user has no completed SplitOS first-run/account association, Runtime Host SHOULD launch/activate Manager in First Run mode.

After onboarding, Manager becomes on-demand unless another product rule explicitly requires visible UX.

### 6.4 Game Launcher startup

Game Launcher MUST be started by Runtime Host only after the canonical prerequisites for Game UX are satisfied.

Normal path:

```text
PRO access proven
→ GAME committed
→ Runtime Host starts/activates Game Launcher
```

Direct managed game launch from WORK still obeys:

```text
Work→Game transaction
→ GAME commit
→ launch Game Launcher / managed game launch continuation
```

---

## 7. Runtime Host initialization order

Runtime Host MUST initialize in a deterministic dependency order.

Recommended sequence:

```text
1. establish ProcessIdentity / ReleaseVersion
2. resolve Windows SessionContext
3. acquire per-session single-instance guard
4. initialize Observability + correlation source
5. open local persistent gateways in read/recovery mode
6. load durable transaction/state headers
7. initialize BrokerClient and probe Broker compatibility
8. initialize Windows integration adapters
9. initialize Product Identity / Runtime Access
10. reconcile incomplete transitions/update/recovery
11. expose Runtime UI IPC
12. activate First Run / Manager / Game Launcher as required
13. enter READY or DEGRADED_READY
```

A failure before step 10 MUST NOT silently invent a new committed mode.

---

## 8. Runtime Host readiness

Runtime Host has the following process-level readiness states:

```text
STARTING
RECONCILING
READY
DEGRADED_READY
STOPPING
```

These are process readiness states, not product OperationalMode.

### READY

All mandatory dependencies for current allowed capabilities are available.

### DEGRADED_READY

Windows user session remains usable, but one or more SplitOS capabilities are unavailable, for example:

- Broker unavailable;
- account backend unavailable and no fresh server check possible;
- optional Game Client adapter failed;
- a nonessential integration is unsupported.

`DEGRADED_READY` MUST NOT be mapped to a fake Work/Game mode.

---

## 9. Shutdown model

### 9.1 Windows sign-out

On sign-out/session termination Runtime Host MUST:

1. stop accepting new UI commands;
2. cancel only operations that are semantically cancelable;
3. flush required local diagnostics/state through owning modules;
4. leave durable mutation transactions in resumable state where applicable;
5. close IPC endpoints;
6. exit.

It MUST NOT perform a new Work/Game transition merely because the Windows user signs out.

### 9.2 OS shutdown/reboot

Shutdown/reboot MUST be treated as interruption evidence by durable transactions.

Runtime Host MUST NOT mark an Update/Recovery/ModeTransition complete only because the process is terminating.

---

## 10. Crash/restart behavior

### 10.1 Runtime Host crash

A Runtime Host crash does not change canonical mode.

On restart:

```text
load durable headers
→ read actual machine evidence
→ reconcile
→ resume/cancel/recover through owning state machines
```

### 10.2 Manager crash

Manager crash has no direct canonical-state consequence.

User may reopen Manager.

### 10.3 Game Launcher crash

If committed mode remains `GAME`, Runtime Host SHOULD attempt controlled Launcher restart.

Repeated Launcher failure MUST surface a degraded/recovery path while preserving correct canonical Game state until an explicit transition succeeds.

### 10.4 Broker crash

Runtime Host MUST move privileged capabilities to unavailable/degraded status and MUST NOT report pending privileged mutations as successful.

---

## 11. Internal module interaction model

Runtime Host modules communicate through explicit in-process semantic contracts.

Preferred conceptual interaction types:

```text
Command
Query
Domain Event / State Change Notification
```

A module MUST NOT:

- write another owner's storage directly;
- derive canonical state from UI presence;
- use diagnostic logs as control state;
- call the Broker directly unless it is the designated BrokerClient/integration boundary;
- call external Game Client/Windows mechanisms except through the appropriate integration adapter.

---

## 12. Operation identity and correlation

Every externally triggered or durable multi-step operation MUST have an `operationId`.

Every IPC request MUST have a unique `requestId`.

Recommended relationship:

```text
correlationId  = end-to-end user/business action
operationId    = one semantic operation/state machine attempt
requestId      = one concrete IPC request
```

Example:

```text
correlationId = user clicked Cyberpunk
operationId-1 = Work→Game transition
requestIds    = broker/display/service calls
operationId-2 = managed game launch
```

Diagnostics MUST propagate these identifiers without treating them as ownership.

Identifiers SHOULD be UUID-compatible opaque values.

---

## 13. Local component version compatibility

Every local process MUST expose:

```text
releaseVersion
protocolMajor
protocolMinor
componentRole
```

### 13.1 Major version rule

Different IPC protocol major versions MUST NOT communicate for mutating operations.

Outcome:

```text
INCOMPATIBLE_PROTOCOL
```

### 13.2 Minor version rule

A newer minor version MAY communicate with an older peer only if the requested capability is explicitly advertised as compatible.

Unknown fields MUST NOT silently change security/semantic behavior.

### 13.3 Release coherence

Updater/installer MUST aim to deploy a release-coherent set.

Runtime compatibility negotiation is a safety fallback, not a substitute for release coherence.

---

## 14. UI process rules

Manager and Game Launcher are presentation/application clients.

They MAY:

- issue semantic commands;
- issue queries;
- subscribe to state/events;
- present user decisions.

They MUST NOT:

- update `OperationalModeState` directly;
- edit durable transaction records;
- call machine-level Windows mutations directly when Broker authority is required;
- infer success from window/process presence;
- grant FREE/PRO locally.

---

## 15. Acceptance criteria

SPEC-01 is satisfied by an implementation only if all are demonstrable:

1. one Runtime Host per Windows session;
2. separate Manager / Game Launcher / Broker processes;
3. interactive processes do not require permanent elevation;
4. exactly one machine Broker service exists;
5. only active physical console session may request machine-wide mode mutation in v1;
6. Runtime Host restart does not invent a new committed mode;
7. Game Launcher exit/crash does not implicitly become WORK;
8. UI cannot mutate canonical state outside Runtime Host semantic contracts;
9. local protocol major mismatch fails closed for mutations;
10. end-to-end operations carry correlation/operation/request identity.

---

## 16. Explicit OPEN carried to later specifications

Not decided here:

- physical local storage engine/schema (`SPEC-03`);
- OAuth/OIDC/token details (`SPEC-04`);
- exact machine mutation queue/lease algorithm (`SPEC-05` / `SPEC-11`);
- concrete Display/Audio/Input/etc Windows algorithms (`SPEC-06`);
- exact Game Client adapter mechanisms (`SPEC-07`);
- detailed update package/recovery technology (`SPEC-11`);
- programming language/framework/DI container choice, unless implementation planning later requires a product-level constraint.

---

## 17. Engineering evidence

Relevant Windows mechanisms validated for this specification:

- Task Scheduler interactive logon type: https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-logontype-principaltype-element
- Active physical console session: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-wtsgetactiveconsolesessionid
- Service failure actions / SCM restart: https://learn.microsoft.com/en-us/windows/win32/api/winsvc/ns-winsvc-service_failure_actionsw
