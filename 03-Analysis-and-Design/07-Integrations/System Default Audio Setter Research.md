# SplitOS — System Default Audio Setter Research

## 1. Purpose

This note resolves the implementation research gate for **IMP-069**:

```text
Can canonical SplitOS v1 change the Windows system-wide default
render/capture endpoint through a documented, supported Windows API?
```

Research date: **2026-09-13**.

This is an integration decision, not a claim that Windows itself cannot change the default device. The requirement is narrower: SplitOS needs a public, documented and supportable programmatic contract suitable for a release-owned MODE transition.

Related canonical specification:

```text
04-Specification/SPEC-06-Windows-Context-Integrations/Audio Integration.md
```

---

## 2. Required acceptance bar

A mechanism is acceptable for automatic `SYSTEM_DEFAULT_AUDIO_SET` only if all of the following are true:

```text
publicly documented Windows contract
+ supported on the SplitOS Windows release set
+ explicit render/capture semantics
+ explicit role semantics where applicable
+ usable from the interactive RuntimeHost boundary
+ deterministic result/error mapping
+ fresh read-back can verify the actual default endpoint
+ no shell/UI scraping
+ no registry reverse engineering
+ no undocumented COM interface dependency
```

A mechanism that merely works on a tested Windows build is not sufficient.

---

## 3. Public Core Audio / MMDevice result

The documented `IMMDeviceEnumerator` contract exposes:

```text
EnumAudioEndpoints
GetDefaultAudioEndpoint
GetDevice
RegisterEndpointNotificationCallback
UnregisterEndpointNotificationCallback
```

This is sufficient for:

```text
endpoint enumeration
current default observation by flow/role
endpoint lookup
change notification
fresh read-back verification
```

The documented interface does **not** expose a method for setting the system-wide default endpoint.

Result:

```text
MMDevice/Core Audio
READ / OBSERVE / VERIFY -> SUPPORTED
SYSTEM DEFAULT MUTATION -> NOT PROVIDED BY THIS PUBLIC CONTRACT
```

---

## 4. Current Windows.Media.Devices result

The current `Windows.Media.Devices.MediaDevice` public surface, including the Windows/WinRT Build 28000 documentation reviewed for this research, exposes audio selectors, current-default getters and default-device-changed events:

```text
GetAudioCaptureSelector
GetAudioRenderSelector
GetDefaultAudioCaptureId
GetDefaultAudioRenderId
DefaultAudioCaptureDeviceChanged
DefaultAudioRenderDeviceChanged
```

No documented system-wide default-device setter is present on this public surface.

Result:

```text
Windows.Media.Devices
DISCOVERY / DEFAULT READ / DEFAULT CHANGE EVENT -> SUPPORTED
SYSTEM DEFAULT MUTATION -> NOT PROVIDED BY THIS PUBLIC CONTRACT
```

---

## 5. Endpoint property store is not a setter substitute

Windows documents the audio endpoint property store for reading endpoint metadata. Current Microsoft documentation states that the Windows audio service sets audio endpoint property values; clients can read them but should not set them.

Therefore SplitOS MUST NOT reinterpret an endpoint property, endpoint ID or `PKEY_AudioEndpoint_StableId` write as a supported way to change the system default.

`PKEY_AudioEndpoint_StableId` remains identity evidence only.

---

## 6. Rejected automatic mechanisms

### 6.1 Undocumented `IPolicyConfig`-family interfaces

Community samples and reverse-engineered desktop utilities commonly use undocumented `IPolicyConfig`/related COM interfaces to change the default endpoint.

Canonical v1 status:

```text
REJECTED as a supported SplitOS release dependency
```

Reasons:

```text
not a documented public Windows contract
interface/version compatibility is not guaranteed
OS update behavior is outside a supported API promise
would turn implementation detail into a commit-critical MODE invariant
```

SplitOS MUST NOT report `APPLIED_VERIFIED` from an undocumented setter merely because a particular build accepted it.

### 6.2 Registry mutation

Rejected.

Reasons:

```text
Registry is not the documented system-default-audio API
schema/ownership can change
writing inferred keys bypasses the supported Windows authority boundary
```

### 6.3 Shell/UI automation

Rejected for automatic mutation.

Examples include automating Settings/Control Panel clicks, keyboard navigation or accessibility-tree scraping.

Reasons:

```text
not deterministic enough for commit-critical MODE transition
localization/UI layout/version sensitive
cannot provide a reliable platform success contract
```

### 6.4 Third-party command tools

Not part of canonical v1.

A future vendor/OEM/tool integration would require its own explicit trust, packaging, versioning, licensing and verification contract. It cannot silently become the generic Windows setter.

---

## 7. Supported user-mediated fallback

Windows documents `ms-settings:` URIs for Sound, including:

```text
ms-settings:sound
ms-settings:sound-devices
ms-settings:sound-defaultinputproperties
ms-settings:sound-defaultoutputproperties
ms-settings:sound-properties?endpointId=<escaped endpoint id>
```

These URIs can take the user to the relevant Windows Settings surface. They do **not** constitute an automatic setter.

Canonical flow:

```text
desired audio target differs from actual default
-> automatic supported setter unavailable
-> return USER_ACTION_REQUIRED
-> optionally open a SplitOS-owned documented Settings URI
-> user changes the Windows default
-> IMMNotificationClient invalidates audioGeneration
-> RuntimeHost performs a fresh AudioSnapshot read
-> compare actual default identity with requested target
```

Opening Settings by itself is never `APPLIED_VERIFIED`.

---

## 8. IMP-069 verdict

Research outcome:

```text
NO_SUPPORTED_AUTOMATIC_SYSTEM_DEFAULT_SETTER_IDENTIFIED
```

Canonical capability status remains:

```text
SYSTEM_DEFAULT_AUDIO_SET -> OPEN
```

Runtime support status for v1:

```text
automatic system-wide default endpoint mutation -> UNSUPPORTED_CAPABILITY
user-mediated Windows Settings fallback          -> SUPPORTED
Core Audio observation + post-user-change verify -> SUPPORTED
```

This resolves the research uncertainty without pretending the mutation capability exists.

---

## 9. Required semantic behavior

When a profile requests an audio endpoint:

```text
fresh default already matches target
-> ALREADY_SATISFIED

selector cannot resolve target
-> TARGET_NOT_FOUND / TARGET_AMBIGUOUS

target unavailable/disabled/disconnected
-> TARGET_UNAVAILABLE

target differs and canonical automatic setter is unavailable
-> USER_ACTION_REQUIRED or UNSUPPORTED_CAPABILITY

user changes default and fresh read-back matches
-> APPLIED_VERIFIED
```

A MODE policy that would require automatic system-default switching MUST NOT classify the action as a silently satisfiable mandatory mutation in canonical v1.

---

## 10. Re-open criteria

`SYSTEM_DEFAULT_AUDIO_SET` may be reconsidered only when at least one of the following becomes true:

```text
Microsoft publishes a documented public Windows API for changing the system default endpoint
or
an approved Windows App SDK / WinRT contract provides equivalent supported semantics
or
SplitOS explicitly adopts a separately versioned vendor/OEM integration with a release support contract
```

Before promotion to supported capability, the candidate MUST pass:

```text
supported Windows build matrix
render + capture behavior
console + multimedia + communications role behavior where exposed
endpoint disconnect/reconnect
non-admin interactive-session behavior
failure/result mapping
fresh GetDefaultAudioEndpoint/read-model verification
driver/OS update compatibility tests
rollback/recovery analysis
```

---

## 11. Official evidence reviewed

Microsoft documentation reviewed for this decision:

```text
IMMDeviceEnumerator
https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immdeviceenumerator

IMMDeviceEnumerator::GetDefaultAudioEndpoint
https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-getdefaultaudioendpoint

Windows.Media.Devices.MediaDevice (Build 28000 view)
https://learn.microsoft.com/en-us/uwp/api/windows.media.devices.mediadevice?view=winrt-28000

MediaDevice.GetDefaultAudioRenderId
https://learn.microsoft.com/en-us/uwp/api/windows.media.devices.mediadevice.getdefaultaudiorenderid?view=winrt-28000

Audio Endpoint Properties
https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-endpoint-properties

PKEY_AudioEndpoint_StableId
https://learn.microsoft.com/en-us/windows/win32/coreaudio/pkey-audioendpoint-stableid

Launch Windows Settings
https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings
```

---

## 12. Result for Slice 04

Slice 04 audio boundary is now explicit:

```text
Core Audio endpoint evidence       -> IMPLEMENTED by IMP-068
Core Audio default observation     -> IMPLEMENTED by IMP-068
Core Audio notifications           -> IMPLEMENTED by IMP-068
StableId feature detection         -> IMPLEMENTED by IMP-068
system-wide automatic default set  -> NOT IMPLEMENTED; unsupported in canonical v1
user-mediated Settings fallback    -> approved design path when product flow needs it
future automatic setter            -> requires a new validated capability decision
```

No undocumented audio mutation primitive is added to RuntimeHost or Broker by IMP-069.
