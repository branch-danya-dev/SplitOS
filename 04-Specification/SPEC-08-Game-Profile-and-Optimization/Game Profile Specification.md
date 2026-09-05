# SPEC-08 — Game Profile Specification

## 1. Purpose

Defines the canonical v1 `GameProfile` contract used by SplitOS to represent one playable scenario for one game.

A profile is persistent desired configuration owned by SplitOS. It is not:

```text
current Windows state
current game process state
external client installation truth
one immutable snapshot of every game setting
```

Canonical relation:

```text
Game
  └── GameProfile [1..n]
        ├── scenario intent
        ├── display/audio/input intent
        ├── performance intent
        ├── managed game-setting policy
        └── explicit user overrides
```

Example:

```text
Cyberpunk 2077
├── Desktop
│   ├── Display: desktop monitor
│   ├── Resolution intent: 2560x1440
│   ├── Refresh intent: high refresh
│   ├── Input: KB&M
│   └── Performance: highest quality that remains stable near high-refresh target
│
└── TV
    ├── Display: living-room TV
    ├── Resolution intent: 3840x2160
    ├── Refresh intent: 60/120 according to current capability
    ├── Input: controller
    └── Performance: highest quality subject to stable living-room target
```

---

## 2. Ownership

Primary owner:

```text
Game Profiles
```

Consumes:

- canonical SplitOS `Game` identity;
- current `HardwareSnapshot`;
- SPEC-06 display/audio/input evidence;
- game-client/install evidence from SPEC-07;
- release-owned game-setting knowledge;
- user actions;
- Optimization recommendations.

Does not own:

- Steam/Epic/Xbox license/install truth;
- physical device capabilities;
- actual game process state;
- actual Windows display/audio/input state;
- game save data.

---

## 3. Canonical profile identity

Every profile has:

```text
profileId
 gameId
 profileName
 scenarioType
 revision
 profileSchemaVersion
 createdUtc
 updatedUtc
```

`profileId` is stable and opaque.

`profileName` is user-facing and not identity.

`scenarioType` v1 values:

```text
DESKTOP
LIVING_ROOM
CUSTOM
```

These are UX/scenario hints, not hardware selectors.

The same `scenarioType` may exist more than once for one game if names/selectors differ.

---

## 4. Profile structure

Normative conceptual payload:

```text
GameProfile
├── Identity
├── ScenarioIntent
├── DisplayIntent
├── AudioIntent
├── InputIntent
├── PerformanceIntent
├── OptimizationMode
├── GameSettingPolicy
├── UserOverrideSet
├── CompatibilityBinding
└── LastResolutionMetadata
```

The physical v1 representation may continue using SPEC-03 versioned JSON fields where appropriate, but each payload MUST validate against a typed schema owned by SPEC-08.

---

## 5. ScenarioIntent

```text
scenarioType
isDefault
optional description
optional preferredUseHint
```

`isDefault` means:

> prefer this profile only after compatibility/eligibility checks.

It does not bypass missing display/input requirements.

At most one default profile per game SHOULD exist physically, using the SPEC-03 atomic `SetDefaultProfile` repository operation.

---

## 6. DisplayIntent

Display intent references a SPEC-06 persistent display selector rather than current CCD `targetId`.

Concept:

```text
DisplayIntent
├── selector
├── selectorRequirement
├── resolutionIntent
├── refreshIntent
├── advancedColorIntent
└── fallbackPolicy
```

### 6.1 selectorRequirement

```text
REQUIRED
PREFERRED
ANY_SUPPORTED
```

`REQUIRED` missing/ambiguous device makes the profile ineligible for automatic launch unless the user explicitly selects an approved fallback.

### 6.2 resolutionIntent

```text
NATIVE
FIXED(width,height)
AUTO_QUALITY
AUTO_PERFORMANCE
```

`FIXED` is desired intent only. SPEC-06 current capability still constrains it.

### 6.3 refreshIntent

```text
MAX_SUPPORTED
FIXED_HZ
AT_LEAST_HZ
DISPLAY_DEFAULT
```

Refresh intent does not assert that the game can render at that frame rate.

### 6.4 advancedColorIntent

Conceptual values:

```text
PREFER_HDR
REQUIRE_HDR
PREFER_SDR
UNCHANGED
```

SPEC-06 capability/version gates apply. `REQUIRE_HDR` cannot silently become successful when HDR mutation/verification is unavailable.

---

## 7. AudioIntent

Audio intent is optional in v1 because SPEC-06 automatic system-default audio mutation remains capability-gated/OPEN.

Concept:

```text
AudioIntent
├── endpointSelector
├── requirement: REQUIRED | PREFERRED | UNCHANGED
└── fallbackPolicy
```

A profile may still persist preferred endpoint identity even when current release can only provide `USER_ACTION_REQUIRED` for default-device switching.

Profile persistence MUST NOT delete the preference merely because the endpoint is currently disconnected.

---

## 8. InputIntent

Concept:

```text
InputIntent
├── interactionClass
├── deviceSelector?
├── selectorRequirement
└── fallbackOrder[]
```

`interactionClass` values:

```text
KEYBOARD_MOUSE
CONTROLLER
AUTO
```

SPEC-06 GameInput device identity is used for specific controller selectors.

Examples:

```text
Desktop profile
→ KEYBOARD_MOUSE

TV profile
→ CONTROLLER
→ preferred DualSense
→ fallback any controller
```

No input intent may enable macros, synthetic aim assistance, injection or anti-cheat-sensitive behavior.

---

## 9. PerformanceIntent

Performance intent describes the optimization objective, not a guaranteed result.

```text
PerformanceIntent
├── priority
├── frameRatePolicy
├── preferredFrameRate?
├── minimumAcceptableFrameRate?
├── framePacingPriority
├── latencyPriority
└── qualityFloor?
```

### 9.1 priority

```text
QUALITY_PRIORITY
BALANCED
PERFORMANCE_PRIORITY
CUSTOM
```

### 9.2 frameRatePolicy

```text
DISPLAY_REFRESH_BOUND
FIXED_TARGET
MAX_STABLE
```

#### DISPLAY_REFRESH_BOUND

Target is derived from current effective display refresh but MUST be constrained by game/hardware capability and policy.

It does **not** mean:

```text
280 Hz monitor → game must reach 280 FPS at any cost
```

If the preferred refresh-derived target cannot be sustained, the optimizer attempts quality degradation within allowed limits and then converges on the highest verified sustainable target according to policy.

#### FIXED_TARGET

User/product explicitly requests a target such as:

```text
60 FPS
120 FPS
```

#### MAX_STABLE

Optimization seeks the best sustainable performance while respecting quality floor/user locks; display refresh remains an upper usefulness bound where applicable.

---

## 10. OptimizationMode

Per profile:

```text
AUTO
SUGGEST_ONLY
USER_MANAGED
```

### AUTO

SplitOS may apply supported game-setting recommendations before launch subject to overrides and safety gates.

### SUGGEST_ONLY

SplitOS computes recommendation and displays differences but does not apply game settings automatically.

### USER_MANAGED

SplitOS manages system context/profile selection but does not alter game graphics settings.

Changing this mode does not change the GameProfile identity.

---

## 11. GameSettingPolicy

This section references release-owned setting definitions supplied by a game configuration adapter.

Concept:

```text
GameSettingPolicy
├── managedSettingKeys[]
├── unmanagedSettingKeys[]
├── qualityFloorRules[]
└── featurePreferences[]
```

A profile never stores arbitrary file paths, INI keys, registry paths or executable commands as setting definitions.

It references release-owned `GameSettingKey` values.

Examples of semantic keys MAY include:

```text
RENDER_RESOLUTION
RENDER_SCALE
UPSCALE_TECHNIQUE
UPSCALE_QUALITY
FRAME_GENERATION
RAY_TRACING_LEVEL
TEXTURE_QUALITY
SHADOW_QUALITY
REFLECTION_QUALITY
CROWD_DENSITY
VSYNC
FRAME_LIMIT
```

Exact availability is game/version/adapter-specific.

---

## 12. UserOverrideSet

Overrides are field-level persistent user intent.

Concept:

```text
UserOverride
├── overrideId
├── settingKey
├── value
├── state
├── createdUtc
└── updatedUtc
```

`state`:

```text
LOCKED
SUSPENDED_CONFLICT
```

A `LOCKED` override means:

> optimization may change other AUTO fields but MUST preserve this value if it remains valid/supported.

A user lock does not override impossible hardware/game constraints.

If the locked value becomes unsupported:

```text
LOCKED
→ SUSPENDED_CONFLICT
```

The value remains stored for user visibility/recovery but is not fabricated into effective configuration.

---

## 13. Precedence

Normative precedence is constraint-first, not file-order-first:

```text
1. Hard platform/game/compatibility constraints
2. Explicit field-level user locks
3. Explicit profile scenario/performance intent
4. Current release optimization recommendation for AUTO fields
5. Release-owned safe/default setting knowledge
6. Game/application default where unmanaged
```

Interpretation:

```text
hard constraint
> user lock
> recommendation
```

but user lock has priority over optimizer preference when both are technically valid.

Example:

```text
User locks Ray Tracing = HIGH
Optimizer predicts target may miss 120 FPS
```

Result:

```text
keep RT HIGH
optimize remaining AUTO fields
if target still unmet → report TARGET_UNMET_WITH_USER_LOCKS
```

Do not silently remove the user's lock to make the benchmark look successful.

---

## 14. Desired, recommended, effective and actual

SPEC-08 preserves four different objects:

```text
Profile Desired Intent
≠
Optimization Recommendation
≠
Resolved Effective Configuration
≠
Actual Game/Windows Evidence
```

### Desired

Persistent user/profile intent.

### Recommendation

Versioned product-owned proposal for AUTO fields.

### Effective

Desired + constraints + overrides + approved fallbacks resolved for one launch.

### Actual

Read-back from game configuration and SPEC-06 Windows/device evidence.

No layer may silently overwrite another merely because values differ.

---

## 15. CompatibilityBinding

A profile does not become invalid merely because current hardware changed.

It stores enough metadata to determine whether recommendations are stale:

```text
lastResolvedReleaseId
lastResolvedGameKnowledgeVersion
lastResolvedGameVersion?
lastResolvedHardwareFingerprint
lastResolvedDisplayIdentity
lastResolvedDriverFingerprint?
lastRecommendationId
```

These fields are metadata, not current truth.

---

## 16. Recommendation invalidation

A previous recommendation becomes `STALE` when a relevant dimension changes, including where applicable:

```text
GPU identity / capability class
VRAM class
CPU capability class
major driver compatibility bucket
selected display / resolution / refresh
GameProfile performance intent
user locks
supported game version / config schema
optimization rule-set version
```

Unrelated changes MUST NOT force full profile destruction.

Example:

```text
controller reconnect
```

normally invalidates input resolution but not the graphics recommendation unless game-specific knowledge says otherwise.

---

## 17. Profile lifecycle

```text
CREATE
→ VALIDATE
→ ACTIVE
→ RESOLVE FOR LAUNCH
→ optional RECOMMENDATION REFRESH
→ APPLY/VERIFY
→ SESSION OBSERVATION
→ RECONCILE
```

Profile stays persistent across temporary device absence.

A failed launch/apply does not automatically delete or rewrite the canonical profile.

---

## 18. Profile cloning

Manager/Launcher MAY support:

```text
CloneProfile(sourceProfileId, newName)
```

Clone creates a new `profileId` and copies desired intent/overrides, not runtime evidence or prior performance samples as canonical truth.

Historical samples MAY remain reference evidence linked to source profile but cannot pretend to have been produced by the clone.

---

## 19. Default profile behavior

A default profile is only a selection hint.

Forbidden:

```text
isDefault = true
→ ignore missing required TV
→ launch anyway with stale TV target
```

Correct:

```text
default profile
→ eligibility evaluation
→ compatible? use
→ incompatible? next deterministic selection/fallback path
```

---

## 20. Persistence

SPEC-03 `game_profile` remains the v1 physical root.

Versioned payloads:

```text
display_selector_json
input_selector_json
optimization_json
game_config_json
```

MUST validate as SPEC-08 schemas.

SPEC-08 MAY add normalized child tables for overrides/recommendation metadata where transactional/query integrity requires it, but implementation MUST preserve:

- profile revision;
- game ownership;
- field-level override semantics;
- no external install/license duplication;
- no actual runtime evidence stored as desired configuration.

---

## 21. Concurrency

Profile writes use SPEC-03 optimistic revision.

Example:

```text
Manager edits profile revision 12
Optimization result was computed from revision 11
```

The stale optimization result MUST NOT overwrite revision 12.

Recommendation/application contracts carry:

```text
profileId
profileRevision
```

and fail/re-resolve on mismatch.

---

## 22. Security and trust

Profile data is user-controlled but not trusted executable input.

MUST NOT contain directly executable:

```text
command lines
raw URI launch strings
file paths to execute
registry paths
service names
scripts
```

Game setting values are interpreted only through a release-owned typed adapter catalog.

---

## 23. Acceptance criteria

A conforming v1 implementation demonstrates:

- multiple profiles per game;
- Desktop and living-room scenarios can coexist;
- profile identity survives device disconnect/reconnect;
- required device ambiguity blocks automatic selection;
- explicit user setting locks survive optimization recalculation;
- unsupported old lock becomes visible conflict instead of fictional success;
- recommendation metadata invalidates on meaningful hardware/game/rules changes;
- stale recommendation cannot overwrite a newer profile revision;
- FREE/USER_MANAGED path does not apply managed game graphics settings;
- desired/recommended/effective/actual remain separate.
