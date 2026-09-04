# SPEC-06 — Input and Hardware Integration

## 1. Purpose

Defines the supported v1 mechanisms for controller/input presence, stable device association, hot-plug handling and generic Windows Plug and Play evidence.

This specification does not implement input cheating, synthetic input, game automation or anti-cheat bypass.

---

## 2. Primary game-input API

SplitOS v1 uses Microsoft GameInput as the primary controller/game-device observation API.

Baseline:

```text
Microsoft.GameInput package/runtime
GameInputCreate
IGameInput
IGameInputDevice
RegisterDeviceCallback
GameInputDeviceInfo / device ID
```

The SplitOS build/release package MUST provision a supported GameInput redistributable version rather than depending on whichever old in-box runtime happens to exist.

The release must preserve GameInput backward-compatibility expectations and must not downgrade a newer installed runtime.

---

## 3. Runtime placement

GameInput runs inside the active user-session `SplitOS.RuntimeHost.exe`.

It is not hosted in the Privileged Broker.

Reason:

```text
input discovery / controller UX
→ interactive-session concern
```

Broker privilege is not required for normal controller enumeration.

---

## 4. Input snapshot

Conceptual `InputSnapshot`:

```text
InputSnapshot
├── generation
├── observedUtc
├── devices[]
│   ├── splitosInputDeviceId
│   ├── gameInputDeviceId
│   ├── connected
│   ├── supportedKinds
│   ├── vendor/product/capability metadata where exposed
│   └── observed status
└── preferred-device resolution result
```

The raw GameInput device identifier is preserved as an opaque binary value encoded for persistence/log-safe transport.

SplitOS MUST NOT infer hidden meaning from bytes in the identifier.

---

## 5. Persistent device identity

GameInput device IDs are the preferred persistent selector identity for game-input devices.

The API documents them as stable across:

```text
application restarts
system reboots
wireless disconnect/reconnect
wired USB disconnect/reconnect when reconnected to the same USB port
```

Therefore a Game Profile may persist a controller selector built primarily from the GameInput device ID.

The profile still needs fallback semantics because:

- a wired device moved to another USB port may resolve as a different device;
- a replacement controller of the same model is not necessarily the same logical device;
- the device may simply be absent.

---

## 6. Device enumeration and hot-plug

Runtime Host creates one retained `IGameInput` instance for its process lifetime.

It registers a device callback for relevant input kinds and connection-status changes.

Callback flow:

```text
GameInput device callback
→ increment inputGeneration
→ update/invalidate device projection
→ notify interested semantic owner
→ owner decides fallback/user action
```

Callback itself MUST NOT:

- switch Work/Game mode;
- change Game Profile automatically without policy;
- launch or terminate games.

---

## 7. Supported input kinds

SplitOS Game Mode v1 is primarily interested in:

```text
gamepad/controller
UI navigation controller
keyboard
mouse
```

Additional GameInput kinds such as racing wheel/flight stick/arcade stick may be observed when supported, but product UX support is capability-driven.

A device being enumerable does not mean Game Launcher has full navigation semantics for that device type.

---

## 8. Preferred input target

A resolved policy/profile target may say:

```text
ANY_GAMEPAD
SPECIFIC_DEVICE
KEYBOARD_MOUSE
PREFER_SPECIFIC_WITH_FALLBACK
```

SPEC-08 owns selection/precedence.

SPEC-06 returns only current evidence:

```text
EXACT_DEVICE_PRESENT
FALLBACK_PRESENT
NO_MATCH
AMBIGUOUS
DISCONNECTED
UNSUPPORTED_KIND
```

---

## 9. Missing controller behavior

If GAME activation/profile requires a controller and the target is absent:

```text
fresh GameInput enumeration
→ selector resolution
→ approved fallback?
```

Possible outcomes:

```text
fallback keyboard/mouse allowed
→ continue with resolved fallback

another gamepad allowed
→ user selects / policy resolves

no fallback
→ USER_ACTION_REQUIRED or TARGET_UNAVAILABLE
```

SplitOS MUST NOT silently bind the first random controller if a specific user-selected device was required.

---

## 10. Input ownership boundary

SplitOS v1 does not claim exclusive OS ownership of a controller.

A selected controller means:

```text
preferred device for SplitOS/Game UX/profile semantics
```

not:

```text
other Windows applications are technically prevented from reading the device
```

Exclusive input capture would require separate OS/API capability validation and is not required for v1.

---

## 11. No synthetic or manipulative input

Canonical v1 integration MUST NOT provide:

```text
synthetic aim correction
rapid-fire/macro injection
anti-recoil automation
input spoofing to game/anti-cheat
virtual controller emulation for competitive advantage
network/matchmaking manipulation
```

GameInput is used for observation/navigation and supported device interaction, not gameplay cheating.

---

# Part B — Generic hardware / PnP

## 12. Generic hardware discovery

For device classes outside specialized Display/Audio/GameInput integrations, SplitOS uses supported Windows Plug and Play APIs, primarily:

```text
Configuration Manager APIs
SetupAPI
CM_Register_Notification
SetupDiGetClassDevs / device-interface enumeration as required
SetupDiGetDeviceInstanceId / unified device properties
```

This layer is evidence-oriented.

It MUST NOT become an arbitrary driver-management API.

---

## 13. PnP notifications

Runtime Host may register `CM_Register_Notification` callbacks for relevant device-interface classes.

Notifications are used for:

```text
arrival
removal/query-removal
interface status changes relevant to SplitOS
```

The callback should be lightweight:

```text
receive event
→ mark hardwareGeneration invalid
→ enqueue refresh
→ return
```

Lengthy reconciliation or mode switching MUST NOT execute inside the PnP callback.

---

## 14. Generic hardware identity

Where a persistent Windows device relationship is required, use documented device instance/interface identity/property model.

Rules:

```text
device instance ID/interface path
→ opaque identity/data
→ do not parse into business meaning unless Windows contract explicitly defines a component
```

A device interface path may be reusable across starts, but product selectors should still preserve enough property evidence to detect replacement/mismatch where correctness matters.

---

## 15. Hardware snapshot

Conceptual `HardwareSnapshot` records:

```text
snapshotId
generation
observedUtc
Windows build/release capability context
GPU/adapter references as observed
Display snapshot reference
Audio snapshot reference
Input snapshot reference
selected additional PnP device evidence
```

`HardwareSnapshot` is interpreted SplitOS evidence, not ownership of physical hardware truth.

---

## 16. Freshness

A hardware snapshot becomes invalid when any relevant specialized/generic generation changes.

Game Launch MUST refresh required hardware evidence immediately before final profile resolution/preparation according to the canonical flow.

Persisted cache may speed startup but cannot satisfy a required fresh launch check by itself.

---

## 17. Failure mapping

| Condition | Result |
|---|---|
| GameInput runtime unavailable | `UNSUPPORTED_CAPABILITY` / repair-required depending release |
| selected controller absent | `TARGET_NOT_FOUND` |
| selected controller disconnected mid-operation | `TARGET_UNAVAILABLE` + generation invalidation |
| multiple weak fallback devices | `TARGET_AMBIGUOUS` |
| PnP notification received | invalidate + refresh, not mode commit |
| generic device property cannot be read | degraded evidence / `TECHNICAL_FAILURE` if mandatory |

---

## 18. Acceptance criteria

- GameInput is the primary v1 game-controller discovery mechanism;
- supported redistributable is part of SplitOS release provisioning;
- Runtime Host retains one GameInput instance and uses device callbacks;
- stable GameInput device IDs drive specific controller selectors;
- same-model devices are not silently interchangeable when specificity matters;
- controller absence follows explicit fallback/user-action policy;
- no synthetic/cheating input capability exists in the supported contract;
- generic PnP uses Configuration Manager/SetupAPI rather than registry scraping;
- hot-plug invalidates snapshots instead of silently mutating canonical mode.
