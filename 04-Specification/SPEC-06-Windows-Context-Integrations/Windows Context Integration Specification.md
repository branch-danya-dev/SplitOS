# SPEC-06 — Windows Context Integration Specification

## 1. Purpose

Defines the concrete Windows integration contracts used by SplitOS Runtime Host and Privileged Broker to observe, apply and verify operating-system context.

This specification implements the semantic contracts from A&D and SPEC-05 without moving ownership into platform adapters.

Core rule:

```text
Mode/Policy owner decides desired state
→ Windows adapter resolves supported mechanism
→ operation submitted
→ actual Windows/device state re-read
→ semantic verification performed
→ owner decides whether transition may continue
```

Therefore:

```text
Win32 API returned success
!= target SplitOS state verified
```

---

## 2. Scope

SPEC-06 covers:

```text
Display
Audio
Input / controller presence
Power scheme
Process evidence
Managed Windows services
Generic hardware / PnP observation
Windows Settings user-mediated fallback
Actual-state evidence and invalidation
```

It does **not** define:

- Game Client discovery/launch — SPEC-07;
- Game Profile selection/optimization rules — SPEC-08;
- Game Launcher UX — SPEC-09;
- Builder component removal decisions — SPEC-10;
- Update/recovery machinery — SPEC-11;
- arbitrary GPU overclock/clock/voltage controls;
- anti-cheat, DRM, networking or synthetic input manipulation.

---

## 3. Windows support model

SplitOS v1 is Windows 11 based.

SPEC-06 MUST NOT hard-code one eternal Windows build assumption. Each SplitOS release has a supported Windows build set supplied by release/build compatibility metadata.

A Windows integration capability has a runtime status:

```text
SUPPORTED_PUBLIC
SUPPORTED_PUBLIC_VERSION_GATED
USER_MEDIATED
UNSUPPORTED
OPEN
```

An operation may proceed only if the current release declares the mechanism supported for the installed Windows build and the runtime capability probe succeeds where required.

Version-gated APIs/properties MUST have fallback semantics rather than being treated as universally present.

---

## 4. Runtime placement

### 4.1 Interactive Runtime Host

The following integrations normally run in `SplitOS.RuntimeHost.exe` under the active Windows user session:

```text
Display read/apply/verify
Audio enumeration/default observation/notifications
GameInput controller discovery/events
Power scheme read/apply/verify
Process evidence
user-facing Settings launch
non-privileged hardware observation
```

Reason: these capabilities describe or affect the interactive user context and should not be moved into Session 0 without need.

### 4.2 Privileged Broker

The Broker owns only bounded privileged execution, including:

```text
managed Windows service state mutation
protected machine component/policy mutation from SPEC-02
machine-state persistence from SPEC-03
```

Runtime Host MUST NOT receive direct admin elevation to bypass Broker.

---

## 5. Common adapter contract

Every context adapter exposes semantically equivalent operations:

```text
ProbeCapabilities()
ReadActualState(requirement)
ResolveTarget(policyTarget, actualState)
ValidateTarget(resolvedTarget)
ApplyTarget(resolvedTarget, operationContext)
ReadBack(operationContext)
Verify(resolvedTarget, readBack)
Invalidate(reason)
```

Not every adapter has a mutation capability. Read-only adapters may reject `ApplyTarget` as unsupported.

---

## 6. Common operation context

Every state-changing call receives:

```text
operationId
correlationId
modeTransitionId where applicable
policyDigest
activationEpochId
snapshotGeneration
fenceToken when privileged machine mutation is involved
cancellation token/deadline
```

Adapters MUST NOT silently apply a target resolved from an invalidated hardware/context snapshot.

---

## 7. Common result model

Windows context results use at least:

```text
ALREADY_SATISFIED
APPLIED_VERIFIED
APPLIED_NOT_VERIFIED
TARGET_NOT_FOUND
TARGET_AMBIGUOUS
TARGET_UNAVAILABLE
UNSUPPORTED_CAPABILITY
VERSION_UNSUPPORTED
USER_ACTION_REQUIRED
PERMISSION_DENIED
STALE_SNAPSHOT
OPERATION_REJECTED
TECHNICAL_FAILURE
VERIFICATION_FAILED
CANCELLED
```

`APPLIED_VERIFIED` is the only generic positive mutation result that asserts this adapter's target condition was re-read and proven.

Even then, the Mode/Update/Recovery owner still decides whether the higher-level transaction may commit.

---

## 8. Evidence envelope

All observed Windows/device evidence MUST include:

```text
sourceKind
observedUtc
snapshotGeneration
availability
identity material appropriate to the device class
capability/version metadata
raw platform code for diagnostics where useful
```

Evidence MUST NOT be represented as an eternal boolean such as:

```text
tvConnected=true
```

without observation generation/freshness context.

---

## 9. Snapshot generation and invalidation

Runtime Host maintains monotonic in-memory generations for context families:

```text
displayGeneration
audioGeneration
inputGeneration
hardwareGeneration
processGeneration
serviceGeneration
```

Relevant operating-system notifications increment/invalidate the generation.

A Mode operation that resolved a target against generation `N` MUST re-resolve or fail with `STALE_SNAPSHOT` if a relevant generation changed before required apply/verify boundaries.

No global fixed TTL is defined in SPEC-06. Later callers may require maximum observation age, but event invalidation + mandatory pre-apply/read-back refresh is the normative correctness mechanism.

---

## 10. Declarative targets only

SPEC-05 Mode Policy remains release-owned and declarative.

Windows adapters accept typed targets such as:

```text
DisplayTargetId
AudioSelector
InputSelector
PowerPolicyId
ManagedServiceId
ApplicationId
```

They MUST NOT accept policy-provided:

```text
PowerShell
cmd.exe command line
arbitrary registry path
arbitrary service name
arbitrary executable path to run as admin
raw driver IOCTL
```

Mappings from SplitOS-owned IDs to Windows mechanism parameters are release/version controlled.

---

## 11. Verification rule

Every mutation used as a `MANDATORY` Mode Policy item follows:

```text
resolve
→ validate
→ apply
→ re-read independent actual state
→ compare
→ APPLIED_VERIFIED or failure
```

Examples:

```text
SetDisplayConfig success
→ QueryDisplayConfig again
→ verify active path + resolution + refresh

PowerSetActiveScheme success
→ PowerGetActiveScheme
→ verify expected scheme GUID

StartService success
→ QueryServiceStatusEx until terminal target/timeout
→ verify SERVICE_RUNNING
```

---

## 12. Source-state capture for rollback

Before a reversible Windows context mutation, the adapter records an operation-scoped source snapshot sufficient to reconstruct the previous supported state.

Source snapshots are part of the mode action journal semantics from SPEC-05.

They MUST be treated as recovery evidence, not blindly replayed after device topology has changed.

Rollback flow:

```text
stored source intent/evidence
→ refresh current hardware
→ resolve equivalent reachable source target
→ apply
→ read back
→ verify
```

---

## 13. Capability degradation

A capability can be absent without making Windows unusable.

Examples:

```text
system default audio setter unavailable
→ audio target may become USER_ACTION_REQUIRED / approved fallback

HDR mutation not validated on current build
→ HDR target cannot be MANDATORY for that release/profile

GameInput unavailable/corrupt
→ controller-specific target unavailable; keyboard/mouse fallback only if policy allows
```

Release compatibility MUST prevent a policy from declaring an unimplemented mechanism mandatory without a valid fallback.

---

## 14. Security boundary

Windows evidence is trusted only for the fact it actually proves.

Examples:

```text
process exists
!= document safely saved

service reports RUNNING
!= application business dependency healthy

monitor EDID says model X
!= user intended this physical screen when two identical screens exist

default audio endpoint observed
!= SplitOS successfully changed it
```

External strings such as device paths and friendly names MUST be normalized/treated as data, never executable input.

---

## 15. API baseline summary

| Area | v1 baseline | Status |
|---|---|---|
| display topology/mode read | `GetDisplayConfigBufferSizes` + `QueryDisplayConfig` | SUPPORTED_PUBLIC |
| display target metadata | `DisplayConfigGetDeviceInfo` | SUPPORTED_PUBLIC |
| display topology/mode apply | `SetDisplayConfig` + read-back | SUPPORTED_PUBLIC |
| HDR/advanced color | DisplayConfig device-info capability family | SUPPORTED_PUBLIC_VERSION_GATED / release validation required |
| audio enumerate/read default | MMDevice/Core Audio | SUPPORTED_PUBLIC |
| audio change notifications | `IMMNotificationClient` | SUPPORTED_PUBLIC |
| persistent audio identity | `PKEY_AudioEndpoint_StableId` on Windows 11 24H2+ when present | SUPPORTED_PUBLIC_VERSION_GATED |
| system-wide default audio set | no documented Core Audio setter adopted by SplitOS v1 baseline | OPEN / USER_MEDIATED fallback |
| controller/input discovery | Microsoft GameInput + redistributable | SUPPORTED_PUBLIC_VERSION_GATED |
| generic PnP notifications | Configuration Manager `CM_Register_Notification` | SUPPORTED_PUBLIC |
| active power scheme | PowrProf APIs | SUPPORTED_PUBLIC |
| process evidence | PSAPI/Kernel32 process APIs | SUPPORTED_PUBLIC |
| managed service apply/read | SCM APIs behind Broker allowlist | SUPPORTED_PUBLIC |
| user Settings fallback | documented `ms-settings:` URIs | SUPPORTED_PUBLIC |

---

## 16. Explicitly rejected v1 mechanisms

The following are not canonical SplitOS integration mechanisms:

```text
undocumented IPolicyConfig as required audio setter
random registry edits for display/audio defaults
PowerShell wrappers for normal mode transition
screen-scraping Windows Settings
arbitrary service-name control from Runtime Host
TerminateProcess as generic Work→Game cleanup
synthetic controller input / aim-assist manipulation
vendor GPU overclock APIs without a separate validated integration contract
```

An engineering prototype may evaluate alternatives, but it cannot silently become supported product behavior.

---

## 17. Acceptance criteria

SPEC-06 is implementable when:

- every Windows context family has a typed adapter contract;
- supported mutations have read-back verification;
- persistent vs ephemeral device identity is explicit;
- hot-plug invalidates stale resolution;
- user-session vs Broker execution boundaries are explicit;
- unsupported system-default audio mutation has a defined user-mediated fallback;
- mode policy contains no raw Windows administrative commands;
- external evidence remains evidence rather than canonical product state.
