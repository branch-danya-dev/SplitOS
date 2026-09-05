# SPEC-08 — Profile Selection and Hardware Matching

## 1. Purpose

Defines how SplitOS selects one `GameProfile` for a launch and how the selected profile is revalidated against current hardware/display/input evidence.

The algorithm MUST be deterministic and explainable.

Forbidden baseline:

```text
score every profile with unexplained magic weights
→ pick highest
```

Preferred baseline:

```text
fresh context
→ eligibility
→ ordered match class
→ deterministic tie-break
→ explicit user choice when still ambiguous
```

---

## 2. Inputs

Selection consumes:

```text
gameId
available profiles for game
explicit selectedProfileId?
last-used profile metadata?
current HardwareSnapshot
SPEC-06 DisplaySnapshot
SPEC-06 InputSnapshot
SPEC-06 AudioSnapshot where relevant
current Game Client / installation evidence
current SplitOS release / compatibility knowledge
```

All required hardware/device snapshots MUST satisfy freshness requirements.

---

## 3. Evaluation result

Every profile is evaluated into:

```text
ELIGIBLE_EXACT
ELIGIBLE_WITH_APPROVED_FALLBACK
INELIGIBLE_MISSING_REQUIRED_TARGET
INELIGIBLE_AMBIGUOUS_TARGET
INELIGIBLE_UNSUPPORTED_CAPABILITY
INELIGIBLE_COMPATIBILITY
STALE_CONTEXT
```

An ineligible profile is not deleted.

---

## 4. Explicit user selection

If the user explicitly selects a profile in Game Launcher or Manager:

```text
selectedProfileId
```

that profile is evaluated first.

If `ELIGIBLE_EXACT`:

```text
use it
```

If only an approved fallback is possible:

```text
show fallback consequence when material
→ user accepts or profile-specific policy allows it
```

If ineligible:

```text
do not silently select a materially different profile
```

The UI may offer alternatives.

---

## 5. Automatic selection order

When there is no explicit profile selection, v1 uses the following ordered decision:

```text
1. eligible profile with exact required-device context matching a stored context preference
2. eligible last-used profile for the same normalized hardware/display scenario
3. eligible game default profile
4. unique remaining ELIGIBLE_EXACT profile
5. unique remaining ELIGIBLE_WITH_APPROVED_FALLBACK profile if fallback is non-material
6. ask user
```

The algorithm MUST NOT jump to another profile solely because its name contains `TV` or `Desktop`.

---

## 6. Normalized hardware scenario

`last-used same scenario` uses a normalized context key, not raw transient handles.

Conceptual inputs:

```text
selected display persistent identity
selected display effective resolution class
selected display refresh class
input interaction class
specific controller identity only when profile requires it
GPU capability fingerprint
```

Transient values such as:

```text
CCD targetId
PID
USB enumeration order
```

MUST NOT form the persistent scenario key.

---

## 7. Display matching

SPEC-06 resolves the profile's persistent selector.

Profile selection consumes the resolution status:

```text
EXACT
UNIQUE_FALLBACK
AMBIGUOUS
NOT_FOUND
STALE_SNAPSHOT
```

Rules:

```text
REQUIRED + EXACT
→ eligible

REQUIRED + UNIQUE_FALLBACK
→ eligible only when fallback explicitly belongs to profile policy/user-approved mapping

REQUIRED + AMBIGUOUS/NOT_FOUND
→ ineligible

PREFERRED + NOT_FOUND
→ may use approved fallback

ANY_SUPPORTED
→ choose current allowed display according to Game Mode policy
```

Display friendly name alone is never sufficient for exact persistent matching.

---

## 8. Refresh/resolution eligibility

A profile can remain eligible even if its preferred display mode is not exactly available, provided its fallback policy permits a supported alternative.

Example:

```text
TV profile desired: 4K 120 Hz
actual current capability: 4K 60 Hz
fallback: allow same display at best verified refresh >= 60
→ ELIGIBLE_WITH_APPROVED_FALLBACK
```

But:

```text
profile requires 120 Hz
actual: 60 Hz
no fallback
→ INELIGIBLE_UNSUPPORTED_CAPABILITY
```

The fallback becomes part of the immutable per-launch resolved profile snapshot.

---

## 9. Input matching

Examples:

```text
KEYBOARD_MOUSE
→ eligible if normal keyboard/mouse interaction available

CONTROLLER + REQUIRED specific device
→ exact GameInput device required

CONTROLLER + PREFERRED specific device
→ exact device preferred; fallback any compatible controller if allowed

AUTO
→ does not independently disqualify profile
```

Hot-plug after selection invalidates the selection context if the device participated in eligibility.

---

## 10. Audio matching

Because automatic default audio mutation may be unavailable in v1, audio intent has separate statuses:

```text
SATISFIED
USER_ACTION_REQUIRED
FALLBACK_AVAILABLE
UNAVAILABLE
```

A `PREFERRED` audio endpoint normally does not make the whole profile ineligible.

A `REQUIRED` endpoint can make automatic launch unavailable until the requirement is satisfied or explicitly overridden.

---

## 11. Hardware fingerprint

Optimization validity uses a normalized `HardwareOptimizationFingerprint`.

Conceptual fields:

```text
primary GPU stable identity / vendor / architecture capability bucket
VRAM capacity bucket
CPU family/capability bucket
logical core/thread class where material
system RAM bucket
selected display identity + resolution/refresh capability class
relevant driver compatibility bucket
```

The fingerprint is not a device tracking/marketing identifier and SHOULD exclude unrelated personally identifying hardware data.

A fingerprint change means:

```text
recommendation may be stale
```

not:

```text
profile invalid forever
```

---

## 12. Material context changes before launch

Game Launcher MUST refresh/revalidate at least:

```text
when Game Launcher becomes active
before PrepareLaunch
on relevant SPEC-06 snapshot generation invalidation
```

Meaningful changes include:

```text
display connect/disconnect/topology change
selected display capability change
controller connect/disconnect when profile depends on it
GPU/driver compatibility change
profile revision change
Game Profile selection change
```

---

## 13. Selection token

Once a profile is resolved for a managed launch, Runtime creates a `ResolvedProfileContext`:

```text
resolvedProfileContextId
profileId
profileRevision
gameId
hardwareSnapshotGeneration
displaySnapshotGeneration
inputSnapshotGeneration
audioSnapshotGeneration where used
resolvedDisplayTarget
resolvedInputTarget
resolvedAudioTarget?
performanceIntent
optimizationRecommendationId?
createdUtc
```

This object is immutable for that launch attempt.

If a relevant generation changes before the dependent action is applied:

```text
ResolvedProfileContext → STALE
```

and the operation must re-resolve or fail explicitly.

---

## 14. Re-evaluation during launch preparation

Canonical path:

```text
profile selected
→ create ResolvedProfileContext
→ refresh external install/client evidence
→ refresh Windows context evidence
→ validate recommendation
→ immediately before dependent apply, validate generations
→ apply/verify
```

No adapter may continue using a stale device target simply because profile selection already happened in the UI.

---

## 15. Last-used profile

After a successful managed launch confirmation, SplitOS MAY persist:

```text
lastUsedProfileId
lastUsedScenarioFingerprint
lastUsedUtc
```

This is selection history, not default/profile ownership.

A failed launch MUST NOT overwrite last-used metadata as though the scenario succeeded.

---

## 16. User default vs last used

```text
user default
!= last used
```

Default is explicit persistent preference.

Last-used is derived convenience history.

Default wins after eligibility check when no stronger same-context preference exists.

---

## 17. Profile prompt conditions

Game Launcher asks the user when:

```text
multiple equally valid materially different profiles remain
explicit profile is unavailable and fallback changes scenario materially
required device identity is ambiguous
stored default is ineligible and alternatives require user trade-off
an override conflict prevents reliable auto resolution
```

The UI SHOULD show the reason in semantic terms:

```text
"Living-room TV is disconnected"
"Two matching controllers were found"
"This profile requires 120 Hz; current link exposes 60 Hz"
```

not raw Windows adapter IDs.

---

## 18. No automatic destructive profile rewriting

If a device is absent:

```text
TV profile
→ keep TV selector
→ current launch may use fallback/other profile
```

SplitOS MUST NOT rewrite the canonical TV profile to the desktop monitor merely because the TV was disconnected at one launch.

---

## 19. Compatibility change

If current release marks the profile's game-setting adapter or feature combination unsupported:

```text
profile persists
→ automatic optimization capability may downgrade
→ user receives compatibility state
```

Example:

```text
AUTO → temporarily SUGGEST_ONLY / USER_MANAGED
```

according to release policy.

---

## 20. Acceptance criteria

- two profiles for same game can map independently to desktop and TV;
- explicit compatible user choice wins;
- raw Windows display target IDs are never persistent profile selectors;
- disconnected required TV makes that profile ineligible without modifying it;
- approved 120→60 Hz fallback is explicit in resolved context;
- multiple same-name displays cannot be silently guessed;
- controller hot-plug invalidates dependent context;
- last-used history does not overwrite default;
- stale snapshot generation prevents apply;
- a material fallback causes user confirmation where policy requires it.
