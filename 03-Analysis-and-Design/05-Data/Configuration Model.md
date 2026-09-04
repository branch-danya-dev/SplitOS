# SplitOS — Configuration Model

## 1. Purpose

Документ определяет концептуальную композицию конфигурации SplitOS.

Цель — отделить:

```text
release baseline
account/user preferences
mode configuration
game-specific configuration
runtime-derived context
external actual state
```

чтобы позже не получить один гигантский `settings.json`, который одновременно хранит продуктовую policy, пользовательские настройки, временный runtime state и внешние evidence.

---

# 2. Configuration layers

SplitOS configuration conceptually состоит из пяти слоёв:

```text
1. Release / Baseline Configuration
2. Installation Configuration
3. User / Account Configuration
4. Mode Configuration
5. Game Configuration
```

Параллельно существуют:

```text
Runtime Derived Context
External Actual State / Evidence
```

Они не должны смешиваться с persistent desired configuration.

---

# 3. Release / Baseline Configuration

Owner:

```text
Distribution Engineering
```

Represents:

```text
SplitOSRelease
BuildManifest
ComponentClassificationDecision
Baseline policies
Package set
Compatibility constraints
```

Properties:

- versioned;
- release-scoped;
- not user-editable in normal runtime;
- establishes supported system assumptions;
- used for drift/recovery validation.

Example:

```text
SplitOS Release 1.2
├── Windows Base X
├── Build Manifest 1.2
├── Defender AV → REMOVE [if validated]
├── Phone Link → MODE_MANAGED
├── Core networking → KEEP
└── SplitOS Runtime Package Set 1.2
```

This layer answers:

> What does a supported SplitOS installation of this release fundamentally consist of?

---

# 4. Installation Configuration

Scope:

```text
one concrete SplitOSInstallation
```

Represents installation-specific but product-owned settings that are not simply user preferences.

Potential concepts:

```text
installed release identity
local device/install identity
selected update channel if product policy permits
local recovery/bootstrap settings
local feature capability state
```

This layer must not become a duplicate source for account entitlement or external Windows license state.

---

# 5. User / Account Configuration

Owner by meaning:

```text
Product Identity & Entitlement / relevant profile owners
```

Represents user-level preferences that may logically survive reinstall/device migration depending on future product policy.

Potential concepts:

```text
preferred UX behavior
notification preferences
profile ownership
controller/navigation preferences
user-level optimization preferences
account-linked Game Profiles where supported
```

Open question:

```text
Which of these are local-only?
Which are account-synced?
```

The answer belongs later to logical/physical modeling.

---

# 6. Mode Configuration

Mode configuration defines desired system/user context for:

```text
WORK
GAME
```

It is **not** the same as current operational mode state.

```text
Mode Configuration
≠
OperationalModeState
```

---

## 6.1 Work Mode Configuration

Conceptually may contain:

```text
Work display preference
Work audio preference
Work input/navigation preference
Work power/resource policy
Work application lifecycle rules
MODE_MANAGED capability targets
Work notification/background policy
```

Example:

```text
Phone Link
WORK → AVAILABLE
GAME → INACTIVE
```

The Windows component itself remains governed by release/component knowledge; Mode Configuration only determines the live target for `MODE_MANAGED` behavior.

---

## 6.2 Game Mode Configuration

Conceptually may contain:

```text
Default Game display
Default Game audio
Default Game input/navigation
Game power/resource policy
Game client lifecycle policy
Shared Apps policy
Game Launcher preferences
MODE_MANAGED capability targets
```

Game Mode Configuration defines general Game environment defaults.

A specific `GameProfile` may refine these defaults for one title/scenario.

---

# 7. Game Configuration

## 7.1 GameProfile

GameProfile is the primary SplitOS configuration object for one gaming scenario.

Conceptual structure:

```text
GameProfile
├── Game reference
├── Profile identity/name
├── Target Display Preference
├── Audio Preference [where profile-specific]
├── Input Profile
├── Performance Target / Optimization Intent
├── Supported Game Setting Preferences
├── User Overrides
└── Compatibility metadata/reference
```

Example:

```text
Cyberpunk 2077 / Desktop
├── Display: desktop gaming monitor
├── Resolution intent: 1440p
├── Refresh intent: high refresh
├── Input: Keyboard & Mouse
└── Optimization: maximize quality while maintaining stable target performance
```

and independently:

```text
Cyberpunk 2077 / TV
├── Display: living-room TV
├── Resolution intent: 4K
├── Refresh intent: 60 Hz
├── Input: Controller
└── Optimization: stable 60 FPS with maximum sustainable quality
```

---

# 8. Desired vs effective vs actual configuration

These three must remain separate.

## 8.1 Desired configuration

What user/policy wants.

Example:

```text
Target display = TV
Target refresh = 120 Hz
```

---

## 8.2 Effective configuration

What SplitOS resolves as applicable after constraints/compatibility are considered.

Example:

```text
Requested TV 120 Hz
Current HDMI/device evidence supports only 60 Hz
→ Effective target = supported safe value / fallback policy
```

Exact resolution algorithm belongs Behavior/Interfaces/Specification.

---

## 8.3 Actual state

What Windows/driver/device reports after application.

```text
Actual display mode
Actual audio endpoint
Actual process/service state
```

Actual state is evidence and must be verified; it must not silently overwrite user intent without explicit reconciliation policy.

---

# 9. Configuration precedence

At conceptual level, configuration resolution should respect semantic authority rather than file order.

A useful precedence model is:

```text
Platform hard constraints
        ↓ constrain
Release / Compatibility policy
        ↓ constrain
Mode defaults
        ↓ refined by
GameProfile
        ↓ refined by
Explicit User Override
        ↓ resolved into
Effective Configuration
        ↓ applied and verified against
Actual State
```

Important:

> Higher preference does not override an impossible or unsupported platform constraint.

Example:

```text
User override: HDR ON
Display capability: HDR unavailable
```

must not produce fictional actual state.

---

# 10. User override semantics

Current product rule:

- SplitOS may recommend/apply settings automatically;
- user can manually change supported settings;
- SplitOS should not blindly erase user changes during the current session.

Conceptually:

```text
Recommended Configuration
≠
User Override
≠
Effective Configuration
```

The canonical representation of persistent user game-setting intent belongs to `GameProfile` ownership.

Open questions:

- whether overrides are field-level or profile snapshot-level;
- when an override expires;
- how recalculation after hardware change reconciles overrides;
- whether the user can reset to SplitOS recommendation.

---

# 11. Device references in configuration

Profiles may reference displays/audio/input devices, but device identity is not guaranteed to be eternal.

Therefore conceptual model distinguishes:

```text
Device Preference
```

from:

```text
Current Device Evidence
```

Example desired semantics:

```text
Preferred Game Display = Living Room TV
```

If the TV is disconnected:

```text
preference remains stored
current resolution uses fallback
```

The absence of device should not destroy the profile unless explicit policy says so.

---

# 12. Application lifecycle configuration

For applications and `MODE_MANAGED` Windows capabilities, configuration should express desired semantic behavior rather than implementation commands.

Prefer:

```text
Phone Link
WORK → AVAILABLE
GAME → INACTIVE
```

not conceptual data like:

```text
run `sc stop ...`
kill process X
set registry Y
```

Implementation actions belong later Specification/integration layer.

---

# 13. Shared App configuration

A Shared App may have Game presentation configuration:

```text
Application
→ SharedAppProfile
   ├── presentation intent
   ├── preferred display
   ├── controller navigation preference
   └── activation policy
```

Current product constraint remains:

```text
maximum active Shared Apps in Game presentation = 3
```

The limit is policy, not necessarily a storage constraint.

---

# 14. Configuration change lifecycle

Conceptual lifecycle:

```text
Default
→ User/Policy Change
→ Validation
→ Canonical Configuration Update
→ Effective Resolution
→ Apply
→ Verify Actual State
```

A failed apply should not automatically imply the canonical desired preference was erased.

Depending on policy, system may:

- keep desired preference and mark unresolved;
- fallback temporarily;
- ask user;
- revise configuration after explicit reconciliation.

Exact behavior is deferred.

---

# 15. Configuration versioning

Not every configuration category has identical version semantics.

## Must be release/version aware

```text
BuildManifest
Component Classification
Compatibility rules
Game integration/config adapter knowledge
```

## User-owned configuration may require migration

```text
GameProfiles
Mode preferences
Shared App profiles
```

Requirement consequence:

> SplitOS updates must define whether stored user configuration remains valid, requires migration, or becomes partially incompatible.

---

# 16. What must not be stored as one configuration truth

Avoid collapsing:

```text
Desired Mode Policy
Actual Windows state
Hardware Snapshot
Transition State
Diagnostic Log
```

into a single "current configuration" object.

They answer different questions.

---

# 17. Result

SplitOS configuration should be thought of as layered intent:

```text
Release Baseline
        ↓
Installation Context
        ↓
User Preferences
        ↓
Mode Configuration
        ↓
GameProfile
        ↓
User Override
        ↓
Effective Runtime Configuration
        ↓
Actual State Evidence
```

This model keeps stable product policy, user intent and transient platform state from becoming competing copies of the same truth.
