# SPEC-06 — Audio Integration

## 1. Purpose

Defines how SplitOS observes Windows audio endpoints, tracks endpoint identity, reacts to device/default changes and handles desired Work/Game audio routing when a supported system-wide setter is unavailable.

Audio integration MUST remain explicit about the difference between:

```text
endpoint exists
!= endpoint is default
!= SplitOS can programmatically make it default
```

---

## 2. Public API baseline

v1 read/notification baseline uses Windows Core Audio / MMDevice:

```text
IMMDeviceEnumerator
EnumAudioEndpoints
GetDefaultAudioEndpoint
GetDevice
RegisterEndpointNotificationCallback
IMMNotificationClient
IMMDevice / property store
```

These mechanisms provide supported endpoint enumeration, default-endpoint observation and change notifications.

---

## 3. Audio snapshot

Conceptual `AudioSnapshot`:

```text
AudioSnapshot
├── generation
├── observedUtc
├── renderEndpoints[]
├── captureEndpoints[]
├── defaultRenderByRole
│   ├── eConsole
│   ├── eMultimedia
│   └── eCommunications
├── defaultCaptureByRole
└── endpoint capability/identity metadata
```

Each endpoint record contains at least:

```text
flow
state
endpointId
stableId when supported/present
friendly name
interface/device properties needed for selection
observedUtc
```

---

## 4. Endpoint identity

### 4.1 Current endpoint ID

`IMMDevice::GetId` endpoint ID is valid for current Windows endpoint lookup and normally survives reboot/reconnect.

However it MUST NOT be treated as eternal persistent identity because Windows/driver updates may change it.

### 4.2 StableId

On Windows 11 version 24H2+ when `PKEY_AudioEndpoint_StableId` is present, SplitOS uses it as the preferred persistent opaque endpoint identity.

The value is:

```text
opaque
case-sensitive
stored exactly
not parsed for meaning
```

A release MUST feature-detect the property instead of assuming it exists on every supported Windows build.

### 4.3 Fallback selector

When StableId is unavailable, persisted selection uses a bounded selector containing current endpoint identity/property hints and MUST be considered weaker/version-sensitive.

After OS/driver change, failed resolution becomes `TARGET_NOT_FOUND`/rebind requirement rather than silently choosing another same-name endpoint.

Friendly name alone is never a unique audio identity.

---

## 5. Endpoint notifications

Runtime Host registers `IMMNotificationClient` and invalidates `audioGeneration` on at least:

```text
OnDeviceAdded
OnDeviceRemoved
OnDeviceStateChanged
OnDefaultDeviceChanged
relevant endpoint property changes
```

Notification callbacks are invalidation signals.

They MUST NOT directly commit Mode state.

A transition that depends on audio must read a fresh snapshot after relevant invalidation.

---

## 6. Default endpoint observation

SplitOS reads defaults separately by:

```text
flow: render | capture
role: eConsole | eMultimedia | eCommunications
```

The adapter does not collapse all roles into one boolean/default string.

Mode/Profile policy may normally target the console/render role while preserving communications semantics unless explicitly configured otherwise.

Exact role policy belongs to SPEC-08/09 product configuration.

---

## 7. System-wide default endpoint mutation

### 7.1 v1 baseline

SplitOS does **not** adopt undocumented `IPolicyConfig`/registry/shell automation as the required supported system-default audio setter.

Current canonical status:

```text
SYSTEM_DEFAULT_AUDIO_SET
→ OPEN
```

until an officially supported, maintainable mechanism is validated for the supported Windows release set.

### 7.2 Consequence

A desired audio target can resolve to:

```text
ALREADY_SATISFIED
USER_ACTION_REQUIRED
UNSUPPORTED_CAPABILITY
TARGET_NOT_FOUND
TARGET_AMBIGUOUS
```

It cannot claim automatic success when SplitOS merely observed the endpoint.

---

## 8. User-mediated fallback

When policy/profile wants a non-current default endpoint and automatic system default switching is unavailable, SplitOS may launch documented Windows Settings pages:

```text
ms-settings:sound
ms-settings:sound-devices
ms-settings:sound-defaultoutputproperties
ms-settings:sound-defaultinputproperties
```

This is a user-mediated action.

Flow:

```text
SplitOS shows requested target
→ user opens Windows Sound Settings
→ user changes endpoint
→ IMMNotificationClient invalidates audio snapshot
→ SplitOS re-reads default endpoint
→ verify target
```

Opening Settings itself MUST return `USER_ACTION_REQUIRED`, never `APPLIED_VERIFIED`.

---

## 9. Mode Policy classification

Until automatic default audio mutation becomes a validated supported capability, release-owned policy SHOULD classify device-specific audio switching as:

```text
PREFERRED
```

rather than `MANDATORY`, unless the product explicitly chooses an interactive blocker/user-action flow.

A profile may request a target endpoint, but policy resolution must know the runtime capability status before constructing a commit-critical plan.

---

## 10. No surprise volume policy

SPEC-06 does not change system/master endpoint volume as part of normal Work/Game activation.

Reason:

```text
audio endpoint selection
!= user volume preference
```

Automatic volume normalization/limiting would require a separate product requirement and user-safety policy.

Observation of volume may be added later if needed for UX, but it is not a target-mode invariant in v1.

---

## 11. Per-application audio routing

Per-application Windows audio routing is not part of the v1 core mode integration baseline.

SplitOS MUST NOT claim that changing its own WASAPI endpoint routes arbitrary external games/applications to that endpoint.

If future Windows APIs or app-specific mechanisms are adopted, they require an explicit capability contract.

---

## 12. Device disconnect behavior

If selected endpoint disconnects:

```text
audioGeneration invalidated
→ fresh snapshot
→ selector resolves approved fallback?
   ├── yes → return fallback resolution to Mode/Game Profile owner
   └── no  → TARGET_UNAVAILABLE
```

The adapter does not autonomously switch operational mode.

For an already-running game, disconnect normally becomes degraded audio/device state; higher-level policy decides whether to prompt, continue, or wait.

---

## 13. Read-back verification

If/when automatic audio mutation becomes supported, the same core rule applies:

```text
set request accepted
→ GetDefaultAudioEndpoint fresh read
→ compare stable/current identity
→ APPLIED_VERIFIED only on match
```

No setter integration may bypass read-back.

---

## 14. Error/result mapping

| Condition | Result |
|---|---|
| endpoint selector not resolved | `TARGET_NOT_FOUND` |
| selector resolves multiple candidates | `TARGET_AMBIGUOUS` |
| endpoint disabled/disconnected | `TARGET_UNAVAILABLE` |
| target already default | `ALREADY_SATISFIED` |
| target differs, no supported setter | `USER_ACTION_REQUIRED` or `UNSUPPORTED_CAPABILITY` |
| Settings launched | `USER_ACTION_REQUIRED` |
| user changes default and read-back matches | `APPLIED_VERIFIED` |
| callback occurs during transition | invalidate + fresh re-read |

---

## 15. Security / trust

Endpoint IDs, friendly names and property values are data.

They MUST NOT be:

- interpolated into shell commands;
- parsed into administrative operations;
- treated as authorization proof;
- used as arbitrary URI fragments without required escaping/validation.

Settings URI construction MUST use only documented SplitOS-owned URI templates and validated endpoint identifiers where supported.

---

## 16. Acceptance criteria

- endpoint enumeration/default read uses Core Audio/MMDevice;
- notification callbacks invalidate state and do not commit modes;
- StableId is preferred on Windows 11 24H2+ when present;
- legacy endpoint ID is not treated as permanent across driver/OS updates;
- friendly name is never unique authority;
- no undocumented default-audio setter is required by canonical v1;
- unsupported automatic switching has explicit user-mediated fallback;
- target verification reads the actual Windows default endpoint;
- audio failure cannot make Windows desktop unusable.
