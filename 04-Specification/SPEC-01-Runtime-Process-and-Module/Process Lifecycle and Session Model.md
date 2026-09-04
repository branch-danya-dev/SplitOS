# SPEC-01 — Process Lifecycle and Session Model

## 1. Purpose

This document defines process startup, restart, session ownership and termination rules for the installed SplitOS runtime.

---

## 2. Machine boot

Expected sequence:

```text
Windows boot
→ Service Control Manager
→ start SplitOSBroker
→ Windows logon UI
→ user signs in
→ Task Scheduler logon trigger
→ start Runtime Host in user session
```

Broker availability before user logon is preferred because Runtime Host may need privileged reconciliation immediately after sign-in.

Broker availability is not a prerequisite for Windows login itself.

---

## 3. Broker lifecycle

Service identity:

```text
Service name: SplitOSBroker
Binary: SplitOS.Broker.Service.exe
Account: LocalSystem (v1 baseline)
Start type: Automatic
Process model: dedicated own process
Network role: none
```

### 3.1 Failure actions

SCM failure recovery baseline:

```text
failure #1 → restart after 2 seconds
failure #2 → restart after 15 seconds
failure #3+ → no automatic restart loop
```

The service MUST NOT configure `SC_ACTION_REBOOT` or `SC_ACTION_RUN_COMMAND` as ordinary crash recovery.

Runtime Host may surface Broker unavailable state and may request supported service recovery through installation/support tooling, but cannot pretend privileged operations succeeded.

### 3.2 Normal broker stop

A normal service stop MUST:

- stop accepting new requests;
- mark/return active non-durable requests as interrupted;
- preserve durable maintenance transaction records through the owning subsystem;
- close all per-session pipes;
- report `SERVICE_STOPPED` cleanly.

---

## 4. Runtime Host start mechanism

v1 uses a Task Scheduler logon-triggered desktop task.

Normative requirements:

- trigger: interactive user logon;
- security context: that logged-on user;
- logon type: interactive token;
- run level: normal/non-elevated;
- hidden background process allowed;
- launch must not require SplitOS Account credentials;
- task registration is machine-installed and applies to supported interactive users.

Runtime Host starts for FREE and PRO users alike.

---

## 5. Session discovery

Runtime Host determines:

```text
Windows SessionId
Windows user SID
session connection state
physical-console ownership
```

Broker independently derives/validates the client session for privileged requests.

Client-provided session IDs are never authoritative.

---

## 6. Physical console ownership

v1 managed machine control is bound to the physical console session.

```text
WTSGetActiveConsoleSessionId()
→ current physical console SessionId
```

If current Runtime Host SessionId differs:

```text
machineMutationEligible = false
```

### 6.1 RDP

An RDP session MUST NOT become v1 Game/Work machine-control owner merely because it is active remotely.

RDP may use account/settings/read-only capabilities where otherwise allowed.

### 6.2 Disconnected session

A disconnected session's Runtime Host MAY remain alive temporarily according to Windows session behavior, but MUST lose machine mutation eligibility.

### 6.3 Locked console

Locking the current console does not automatically change committed operational mode.

New user-driven managed mutations SHOULD be denied/deferred while no eligible interactive control session is available.

---

## 7. Fast User Switching

Example:

```text
Session 1 / User A / physical console
        ↓
User switches to User B
        ↓
Session 1 remains logged on but no longer console owner
Session 2 becomes physical console
```

Rules:

1. User A Runtime Host MUST stop initiating machine-wide mutations.
2. Current canonical machine mode remains unchanged unless an owning flow changes it.
3. User B Runtime Host MUST reconcile canonical/durable state against actual machine state before exposing managed mutation controls.
4. If a major durable operation is in progress, User B observes it; it does not create a parallel one.
5. A Game Session associated with User A does not become User B's managed Game Session.

Exact product UX for fast-user-switch while `GAME_RUNNING` is deferred to `SPEC-09`, but state safety rules above are normative.

---

## 8. Runtime Host process readiness lifecycle

```text
PROCESS_START
    ↓
STARTING
    ↓
RECONCILING
    ├── safe + full dependencies → READY
    └── safe + missing capability → DEGRADED_READY

READY / DEGRADED_READY
    ↓
STOPPING
    ↓
PROCESS_EXIT
```

### STARTING

Process identity, session identity, diagnostics, local stores and Broker compatibility are initialized.

### RECONCILING

Runtime reads durable headers and actual evidence to determine whether any previous operation is incomplete.

### READY

Current permitted product capabilities have required dependencies.

### DEGRADED_READY

Base Windows remains usable, but one or more SplitOS capabilities are unavailable.

---

## 9. First Run lifecycle

```text
Runtime Host READY/DEGRADED_READY
→ no completed account association/onboarding
→ activate Manager / First Run
→ user signs in or creates SplitOS account
→ entitlement resolved
→ FREE or PRO behavior
```

Failure to complete SplitOS First Run MUST NOT block the Windows desktop indefinitely.

The user must have a recoverable path to complete/retry SplitOS setup from Manager.

---

## 10. Manager lifecycle

Manager is on-demand after first run.

Activation sources MAY include:

- Start menu;
- SplitOS status/control entry;
- First Run request from Runtime Host;
- upgrade/subscription prompt;
- recovery/degraded-state prompt.

Manager exits without stopping Runtime Host.

Manager does not own background runtime availability.

---

## 11. Game Launcher lifecycle

### Enter Game Mode

```text
ModeTransition verifies target
→ ModeState commits GAME
→ Runtime Host launches/activates Game Launcher
→ Launcher connects to Runtime Host
→ Launcher receives GAME read model
```

### Normal game launch

Launcher remains the Game UX parent/surface; external game/client processes remain external.

### Game exits

```text
Game exit evidence
→ GameSession returns toward Launcher
→ Launcher remains/returns visible
→ committed mode remains GAME
```

### Launcher exits unexpectedly

Runtime Host SHOULD attempt restart if:

- `CommittedMode=GAME`;
- user session remains eligible;
- no shutdown/recovery operation explicitly suppresses Launcher.

If restart repeatedly fails, Runtime Host exposes degraded control path rather than committing WORK automatically.

---

## 12. Runtime Host crash lifecycle

Crash scenario:

```text
Runtime Host terminates unexpectedly
→ canonical durable state remains as last committed
→ process is restarted by startup/recovery mechanism or user/session action
→ RECONCILING
→ actual-state verification
→ READY / DEGRADED_READY / Recovery
```

No state transition is inferred from the crash itself.

A process watchdog MUST NOT simply write `WORK` or `GAME` as a crash remedy.

---

## 13. Runtime Host restart policy

The startup task SHOULD be configured to restart Runtime Host after unexpected process failure, but implementation MUST avoid an infinite rapid crash loop.

Recommended semantic policy:

```text
limited automatic retries
→ if repeated failure
→ stop automatic loop
→ preserve Windows usability
→ surface repair/support action
```

Exact Task Scheduler retry numeric configuration may be finalized in packaging/deployment implementation because it does not change canonical state semantics.

---

## 14. Sign-out

When Windows session signs out:

```text
UiGateway stops accepting new commands
→ owners persist required durable state
→ cancel cancelable non-durable work
→ leave durable transactions resumable
→ close session IPC
→ Runtime Host exits with session
```

Sign-out is not an implicit mode transition.

---

## 15. Reboot / shutdown

Broker and Runtime Host must tolerate OS termination.

For durable operations:

```text
process termination
!= transaction completion
```

On next boot/sign-in owning modules inspect durable transaction state and actual evidence.

---

## 16. Version mismatch lifecycle

If Runtime Host starts and discovers incompatible Broker major protocol:

```text
Runtime Host
→ DEGRADED_READY
→ privileged capabilities disabled
→ base Windows remains usable
→ update/repair path surfaced
```

If Manager/GameLauncher is incompatible with Runtime Host:

```text
client activation rejected with INCOMPATIBLE_PROTOCOL
→ user-visible repair/update action
```

No compatibility fallback may expose a broader privileged API.

---

## 17. Session test matrix

| Scenario | Expected result |
|---|---|
| one local user | one Host, console owner eligible |
| two users via Fast User Switching | one Host each; only current physical console Host mutation-eligible |
| RDP user + local console user | RDP Host cannot own v1 machine mode; local console may |
| console locked | mode unchanged; new mutations deferred/denied if no eligible interactive owner |
| Host crash | canonical state unchanged; reconcile after restart |
| Manager crash | no state impact |
| Launcher crash in GAME | controlled restart/degraded GAME path; no implicit WORK |
| Broker crash | privileged operations unavailable; no false success |
| user sign-out during normal idle state | Host exits; no automatic mode commit |
| reboot during durable update/recovery | resume/reconcile from durable transaction evidence |

---

## 18. Engineering evidence

- `WTSGetActiveConsoleSessionId`: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-wtsgetactiveconsolesessionid
- Task Scheduler `InteractiveToken`: https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-logontype-principaltype-element
- Service failure actions: https://learn.microsoft.com/en-us/windows/win32/api/winsvc/ns-winsvc-service_failure_actionsw
- `ChangeServiceConfig2` failure/hardening configuration: https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-changeserviceconfig2w
