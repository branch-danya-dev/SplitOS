# SPEC-06 — Windows API Compatibility Matrix

## 1. Purpose

Records the current v1 API mechanism status separately from product semantics.

Statuses:

```text
SUPPORTED_PUBLIC
SUPPORTED_PUBLIC_VERSION_GATED
USER_MEDIATED
OPEN
REJECTED
```

A supported public API still requires SplitOS release/build compatibility testing before being marked supported for a concrete release.

---

## 2. Display

| Capability | Mechanism | Status | Notes |
|---|---|---|---|
| active topology/mode snapshot | `GetDisplayConfigBufferSizes` + `QueryDisplayConfig` | SUPPORTED_PUBLIC | primary CCD read path |
| monitor target metadata | `DisplayConfigGetDeviceInfo(GET_TARGET_NAME)` | SUPPORTED_PUBLIC | includes EDID manufacturer/product fields, connector instance, friendly name, monitor device path |
| SetupAPI correlation | monitor device path → SetupAPI / device instance identity | SUPPORTED_PUBLIC | persistent selector evidence |
| validate supplied config | `SetDisplayConfig(SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG)` | SUPPORTED_PUBLIC | required before normal apply |
| apply supplied config | `SetDisplayConfig(SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG)` | SUPPORTED_PUBLIC | read-back required |
| permanently save mode topology | `SDC_SAVE_TO_DATABASE` | SUPPORTED_PUBLIC but REJECTED for normal mode switch | only explicit durable-user-setting feature may use it |
| Windows persisted topology fallback | `SDC_USE_DATABASE_CURRENT` | SUPPORTED_PUBLIC | explicit BASE/recovery fallback only after compatibility validation |
| virtual/dynamic refresh awareness | CCD Windows 11 path flags | SUPPORTED_PUBLIC_VERSION_GATED | preserve unless intentionally changed |
| advanced color/HDR info | DisplayConfig device info advanced-color/HDR families | SUPPORTED_PUBLIC_VERSION_GATED | build/structure validation required |
| advanced color/HDR write | DisplayConfig SET advanced-color/HDR families | SUPPORTED_PUBLIC_VERSION_GATED | cannot be mandatory until release-tested |
| DDC/CI brightness/contrast | Monitor Configuration API | supported platform family, outside v1 baseline | future optional capability |

---

## 3. Audio

| Capability | Mechanism | Status | Notes |
|---|---|---|---|
| enumerate endpoints | `IMMDeviceEnumerator::EnumAudioEndpoints` | SUPPORTED_PUBLIC | render/capture |
| read default endpoint | `GetDefaultAudioEndpoint` | SUPPORTED_PUBLIC | flow + role aware |
| observe endpoint/default changes | `IMMNotificationClient` | SUPPORTED_PUBLIC | invalidation signal |
| current endpoint lookup | endpoint ID + `GetDevice` | SUPPORTED_PUBLIC | endpoint ID not eternal across driver/OS changes |
| persistent stable endpoint identity | `PKEY_AudioEndpoint_StableId` | SUPPORTED_PUBLIC_VERSION_GATED | Windows 11 24H2+ when property available |
| set system default endpoint | no adopted documented Core Audio setter | OPEN | do not use undocumented `IPolicyConfig` as product baseline |
| user default-output fallback | `ms-settings:sound*` documented Settings URIs | USER_MEDIATED | verify after user action |
| system/master volume mutation | Core Audio has mechanisms, but product behavior not required | outside v1 baseline | avoid surprising user volume changes |
| arbitrary-game per-app route | not adopted | OPEN | separate future capability |

---

## 4. Input

| Capability | Mechanism | Status | Notes |
|---|---|---|---|
| controller/game device API | Microsoft GameInput | SUPPORTED_PUBLIC_VERSION_GATED | provision supported redistributable |
| create runtime | `GameInputCreate` | SUPPORTED_PUBLIC_VERSION_GATED | retain process singleton |
| enumerate/connect/disconnect | `IGameInput::RegisterDeviceCallback` | SUPPORTED_PUBLIC_VERSION_GATED | callback invalidates snapshot |
| persistent controller identity | GameInput device ID | SUPPORTED_PUBLIC_VERSION_GATED | opaque ID; stable under documented conditions |
| keyboard/mouse observation | GameInput supported kinds / Windows input evidence | SUPPORTED_PUBLIC_VERSION_GATED | UX support remains product capability |
| exclusive controller ownership | none required | OPEN/not required | selection is preference, not exclusive OS lock |
| synthetic gameplay input | none | REJECTED | no aim/macro/spoofing baseline |

---

## 5. Generic PnP / hardware

| Capability | Mechanism | Status | Notes |
|---|---|---|---|
| PnP arrival/removal notification | `CM_Register_Notification` | SUPPORTED_PUBLIC | Windows 8+; callback should only invalidate/enqueue refresh |
| enumerate device interfaces | Configuration Manager / SetupAPI | SUPPORTED_PUBLIC | use relevant class/interface filters |
| retrieve device interface detail | `SetupDiGetDeviceInterfaceDetail` | SUPPORTED_PUBLIC | device path is opaque |
| retrieve device instance ID | `SetupDiGetDeviceInstanceId` / unified property model | SUPPORTED_PUBLIC | persistent identity evidence |
| arbitrary driver install/remove | SetupAPI exists but not Runtime responsibility | REJECTED for Mode Runtime | Builder/update own controlled driver/package lifecycle |

---

## 6. Power

| Capability | Mechanism | Status | Notes |
|---|---|---|---|
| read active scheme | `PowerGetActiveScheme` | SUPPORTED_PUBLIC | current user |
| set active scheme | `PowerSetActiveScheme` | SUPPORTED_PUBLIC | verify by read-back |
| enumerate schemes | `PowerEnumerate` | SUPPORTED_PUBLIC | compatibility validation |
| power setting notifications | PowrProf notification APIs | SUPPORTED_PUBLIC | optional invalidation/observability |
| GPU/CPU overclock/voltage | vendor-specific | OPEN/outside v1 | separate safety contract required |

---

## 7. Processes

| Capability | Mechanism | Status | Notes |
|---|---|---|---|
| enumerate process IDs | `EnumProcesses` / equivalent supported Win32 snapshot | SUPPORTED_PUBLIC | dynamic buffer handling |
| query executable image | `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageName` | SUPPORTED_PUBLIC | permission may limit evidence |
| map process to Windows session | `ProcessIdToSessionId` | SUPPORTED_PUBLIC | session-scoped blocker evidence |
| protect against PID reuse | `GetProcessTimes` creation time when readable | SUPPORTED_PUBLIC | combine with PID when needed |
| generic safe-save detection | no generic process API proves it | OPEN/app-specific | process presence only |
| generic force kill in mode switch | `TerminateProcess` exists but is not supported behavior | REJECTED | data-safety invariant |

---

## 8. Services

| Capability | Mechanism | Status | Notes |
|---|---|---|---|
| open SCM/service | `OpenSCManager` / `OpenService` | SUPPORTED_PUBLIC | Broker only for mutation path |
| read status | `QueryServiceStatusEx` | SUPPORTED_PUBLIC | final state evidence |
| start service | `StartServiceW` | SUPPORTED_PUBLIC | return is not RUNNING proof |
| stop service | `ControlService(SERVICE_CONTROL_STOP)` | SUPPORTED_PUBLIC | return is not STOPPED proof |
| wait/verify | bounded `QueryServiceStatusEx` loop | SUPPORTED_PUBLIC | honor operation deadline |
| arbitrary service-name action | generic SCM can do it but SplitOS protocol forbids | REJECTED | use ManagedServiceId catalog |
| create/delete/reconfigure services during mode switch | SCM supports it but wrong lifecycle | REJECTED for Mode Runtime | Builder/install/update responsibility |

---

## 9. Windows Settings fallback

Documented `ms-settings:` routes may be used only as user-mediated UX.

Relevant current routes include:

```text
ms-settings:sound
ms-settings:sound-devices
ms-settings:sound-defaultoutputproperties
ms-settings:sound-defaultinputproperties
```

Launching a Settings page proves only that Windows accepted the URI launch request.

It does not prove the user changed the requested setting.

---

## 10. Release validation rule

For every concrete SplitOS release:

```text
API exists in documentation
+
header/runtime capability available
+
supported Windows build included in release matrix
+
empirical apply/read-back tests pass
↓
SUPPORTED for that SplitOS release
```

Documentation alone is not enough for destructive/commit-critical mutation support.

If a new Windows build changes behavior:

```text
new evidence
→ update compatibility decision
→ affected SPEC-06 capability status
→ Mode/Profile fallback behavior
→ verification cases
```
