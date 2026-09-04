# SplitOS — Windows Runtime Integration

## 1. Purpose

Документ конкретизирует Windows-side integration mechanisms для semantic contracts из `06-Interfaces`.

Цель — определить технически реалистичные Windows integration paths без преждевременного превращения их в implementation code.

---

## 2. Integration split

Windows runtime integrations делятся на две группы:

```text
USER_SESSION
PRIVILEGED_SYSTEM
```

### USER_SESSION

Operations tied to the interactive desktop/session:

```text
current Windows user/session
interactive display topology
Game Client processes
window/process evidence
user audio endpoints
Shared App windows
Game Launcher UX
```

### PRIVILEGED_SYSTEM

Operations that may require machine-level privilege:

```text
protected service lifecycle
protected machine policy
system recovery
protected package/config changes
update apply
some MODE_MANAGED machine components
```

---

# 3. Windows user/session integration

## INT-WIN-USER-001 — Active user session evidence

**Semantic contract:** `EXT-WIN-001`.

### Mechanism baseline

Use Windows session APIs such as:

```text
WTSGetActiveConsoleSessionId
WTSQuerySessionInformation / related session APIs
WTS session-change notifications where needed
```

### Status

`VERIFIED` mechanism family.

### Rule

Windows remains authority for session/user identity.

Runtime Host converts platform evidence into:

```text
WindowsUserContext
```

but never creates a second Windows identity.

### Multi-user implication

Do not assume:

```text
one machine = one Windows user
```

Runtime integration must be session-aware.

---

# 4. Session 0 / privileged service boundary

Windows services run outside the interactive desktop context. Therefore:

```text
Privileged Broker
!= interactive Runtime Host
```

### Target design

```text
User Session                  Session 0
------------                  ---------
Runtime Host   ← secured IPC → Privileged Broker
Manager
Game Launcher
```

### Status

`VERIFIED` platform constraint + `CANDIDATE` SplitOS component split.

### Rejected design

```text
LocalSystem Windows Service
→ directly owns Game Launcher UI
→ manipulates user desktop interactively
```

Status: `REJECTED`.

---

# 5. IPC integration

## INT-WIN-IPC-001 — Runtime Host ↔ Privileged Broker

### Preferred mechanism

```text
Windows Named Pipe
```

with:

```text
explicit security descriptor
restricted ACL
caller/session validation
request correlation
protocol version
strict allowlisted operations
```

### Status

`CANDIDATE / preferred`.

### Why not localhost HTTP by default

Local HTTP would create additional port/network attack surface without giving SplitOS meaningful benefit for local privileged control.

It is not forbidden for other non-privileged local scenarios, but should not be the default privileged-control channel.

### Security rule

Default pipe permissions are not sufficient as product policy.

The broker must explicitly define who may connect.

---

# 6. Display integration

## INT-WIN-DISPLAY-001 — Read topology/capabilities

### Mechanisms

```text
GetDisplayConfigBufferSizes
QueryDisplayConfig
DisplayConfigGetDeviceInfo
```

### Status

`VERIFIED`.

### Provides

```text
active paths
topology
source/target mode information
display target identity/friendly metadata
preferred mode evidence
```

### Runtime placement

Prefer the interactive Runtime Host because CCD operations can depend on access to the active console/desktop session.

---

## INT-WIN-DISPLAY-002 — Apply topology/mode

### Mechanism

```text
SetDisplayConfig
```

### Status

`VERIFIED` mechanism family.

### Required SplitOS pattern

```text
DesiredDisplayContext
→ translate to CCD operation
→ SetDisplayConfig
→ immediate Win32 result
→ QueryDisplayConfig
→ compare target vs actual
→ semantic result
```

### Important distinction

`ERROR_SUCCESS` from apply call means the platform accepted/applied the requested operation according to API semantics; SplitOS still performs read-back because the product target contains higher-level expectations.

---

## INT-WIN-DISPLAY-003 — HDR / advanced color

Windows DisplayConfig device-info families expose advanced color information on supported Windows versions/drivers.

### Status

`CANDIDATE` for SplitOS use.

Before canonical support, validate:

```text
Windows build support
GPU driver behavior
monitor capability reporting
HDR enable/disable semantics
read-back stability
```

### VRR

VRR capability/control must remain `OPEN` until a reliable supported Windows/vendor mechanism is validated.

Do not infer VRR purely from refresh-rate data.

---

# 7. Audio integration

## INT-WIN-AUDIO-001 — Endpoint discovery

### Mechanism

Windows Core Audio / MMDevice API:

```text
IMMDeviceEnumerator
EnumAudioEndpoints
GetDefaultAudioEndpoint
IMMDevice
```

### Status

`VERIFIED` for discovery/read.

---

## INT-WIN-AUDIO-002 — Audio device change events

### Mechanism

```text
IMMDeviceEnumerator::RegisterEndpointNotificationCallback
IMMNotificationClient
```

### Status

`VERIFIED`.

Useful for:

```text
headset connected/removed
default endpoint changed
endpoint state changed
device property changed
```

These events can invalidate effective Game/Profile audio context.

---

## INT-WIN-AUDIO-003 — Set default system audio endpoint

### Status

`OPEN`.

Reason:

Public Core Audio APIs clearly support enumeration, default endpoint lookup and notifications, but the current analysis has not established a supported public API for changing the system-wide default endpoint in the general desktop case.

### Rule

Do not canonize undocumented `IPolicyConfig`-style techniques as supported SplitOS baseline without explicit compatibility decision.

Possible future strategies:

```text
supported Windows API if validated
per-application routing where available
user-assisted system setting
vendor/OEM integration
```

---

# 8. Power integration

## INT-WIN-POWER-001 — Active power scheme

### Mechanisms

```text
PowerGetActiveScheme
PowerSetActiveScheme
PowerEnumerate
```

### Status

`VERIFIED`.

### Pattern

```text
DesiredPowerContext
→ select supported scheme
→ PowerSetActiveScheme
→ PowerGetActiveScheme
→ verify
```

### Important limitation

Power scheme alone is not the same as full performance policy.

GPU clocks, vendor-specific boost controls and thermal behavior are separate integration areas.

---

# 9. Process integration

## INT-WIN-PROC-001 — Process enumeration

### Candidate mechanisms

```text
CreateToolhelp32Snapshot / Process32First / Process32Next
process handles / wait APIs
other Win32 process enumeration where better suited
```

### Status

`VERIFIED` mechanism family.

### Uses

```text
Game Client availability
Game process start/exit evidence
application lifecycle observation
pre-flight process inventory
```

### Critical limit

```text
process exists
!= application is safe to close
```

---

## INT-WIN-PROC-002 — Game start/exit evidence

No universal process rule is sufficient for every game.

Potential adapter strategy:

```text
client launch identity
+
known game executable/process identity
+
process tree evidence
+
window evidence where useful
+
client-specific launch state
```

### Status

`CANDIDATE`, per-game/per-client compatibility testing required.

Cases to handle:

```text
bootstrapper → real game process
launcher → child game process
anti-cheat process before game
multiple game processes
process exits while crash reporter remains
```

---

# 10. Service Control Manager integration

## INT-WIN-SVC-001 — Service evidence/control

### Mechanism family

Windows Service Control Manager APIs:

```text
OpenSCManager
OpenService
QueryServiceStatusEx
StartService
ControlService
NotifyServiceStatusChange where suitable
```

### Status

`VERIFIED`.

### Runtime placement

Read-only operations may be possible with limited rights.

Mode-managed mutations requiring privilege go through Privileged Broker.

### Rule

SplitOS must request only named, classified service actions from Component Matrix / Mode Policy.

Forbidden:

```text
stop arbitrary service by user-supplied name
```

---

# 11. Application safe-close integration

Generic Windows process APIs cannot reliably answer:

```text
Does this application have unsaved user work?
```

Therefore Work→Game pre-flight uses tiers.

## Tier A — Known safe integration

App-specific supported integration can report/save/close safely.

Status: future per-application integration.

## Tier B — Generic graceful close attempt

For compatible desktop applications SplitOS may request graceful close and observe result, subject to application policy.

Status: `CANDIDATE`, safety testing required.

## Tier C — Unknown internal state

SplitOS must surface the blocker to the user rather than claim automatic safety.

### Restart Manager

Windows Restart Manager exists primarily for installer/update scenarios and should not be treated as a generic proof that arbitrary user documents are safely saved.

Status for Work→Game automatic-save semantics: `REJECTED as universal solution`.

---

# 12. Window / Shared App integration

Shared App placement may require Win32 window-management integration.

Potential mechanism family:

```text
EnumWindows
window ownership/process mapping
GetWindowRect / SetWindowPos
monitor/window placement APIs
DWM APIs where needed
```

### Status

`CANDIDATE`.

### Constraint

SplitOS should manage presentation only for applications explicitly classified/approved as Shared Apps.

It does not own third-party app internal UI/state.

---

# 13. Input integration

Input context has two distinct layers.

## System/navigation input

SplitOS Game Launcher can consume standard Windows controller/keyboard/mouse input through supported app/framework mechanisms.

Status: `CANDIDATE`, implementation framework dependent.

## Gameplay input

SplitOS must not synthesize unfair gameplay actions.

It may:

```text
select controller-first UX
observe device availability
choose Game Profile based on input device
```

It must not:

```text
create aim automation
modify anti-cheat-sensitive input path
inject gameplay advantage
```

---

# 14. Machine policy / registry integration

Registry/policy writes are allowed only when they correspond to explicit SplitOS-owned baseline/runtime policy.

Pattern:

```text
ModePolicy / Component Matrix
→ allowlisted policy operation
→ broker if privilege required
→ registry/policy mechanism
→ read-back
```

### Status

`CANDIDATE` per policy item.

Do not use Registry as universal integration API when a documented Windows API exists.

---

# 15. Windows integration matrix

| Capability | Mechanism | Context | Status |
|---|---|---|---|
| user/session | WTS/Win32 session APIs | user/service evidence | VERIFIED |
| local privileged IPC | named pipe + explicit ACL | user↔service | CANDIDATE |
| display read | CCD QueryDisplayConfig family | user session | VERIFIED |
| display apply | SetDisplayConfig | user session | VERIFIED |
| HDR advanced color | DisplayConfig advanced color device info | user session | CANDIDATE |
| VRR control | TBD | TBD | OPEN |
| audio enumerate/read | MMDevice API | user session | VERIFIED |
| audio notifications | IMMNotificationClient | user session | VERIFIED |
| system default audio switch | TBD supported API | user session | OPEN |
| power schemes | PowrProf | user/system | VERIFIED |
| process enumeration | Toolhelp/Win32 process APIs | user session | VERIFIED |
| game process identity | adapter + process evidence | user session | CANDIDATE |
| service state | SCM APIs | broker where needed | VERIFIED |
| safe-save generic apps | no universal mechanism | — | OPEN |
| Shared App windows | Win32/DWM/window APIs | user session | CANDIDATE |

---

# 16. Official evidence references

```text
https://learn.microsoft.com/en-us/windows/win32/services/interactive-services
https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-wtsgetactiveconsolesessionid
https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig
https://learn.microsoft.com/en-us/windows/win32/coreaudio/core-audio-interfaces
https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immdeviceenumerator
https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient
https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme
https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/nf-tlhelp32-createtoolhelp32snapshot
```

---

# 17. Result

Windows integration baseline becomes:

```text
Interactive concerns
→ Runtime Host in user session

Machine privileged concerns
→ narrow Privileged Broker

State change
→ supported Windows mechanism
→ read actual state
→ verify against SplitOS target
```

The remaining integration uncertainty is explicit rather than hidden behind generic phrases such as “Windows API”.