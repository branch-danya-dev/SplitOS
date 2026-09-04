# SplitOS — Conceptual Domain Model

## 1. Purpose

Документ определяет концептуальную модель данных SplitOS после фиксации boundaries, responsibilities, ownership, states и behavior.

На этом уровне мы отвечаем на вопросы:

```text
Какие понятия существуют в системе?
Что означает каждое понятие?
Кто владеет его смыслом?
Как понятия связаны?
Какие данные являются каноническими, а какие — evidence/projection?
```

Документ намеренно **не** определяет:

- SQL tables;
- конкретную СУБД;
- JSON schema;
- physical storage;
- IPC/API contracts;
- class names implementation layer.

Главный принцип:

> Data model начинается со смысла и ownership, а не с таблиц.

---

# 2. Domain areas

Концептуально данные SplitOS делятся на следующие области:

```text
Identity & Entitlement
Distribution & Release
Installed Baseline
Operational Mode
Applications
Gaming
Hardware / Devices
Configuration / Profiles
Compatibility / Update
Recovery / Diagnostics
External Evidence / Projections
```

Эти области не означают отдельные базы данных или сервисы.

---

# 3. Data categories

Каждый значимый объект данных должен относиться к одному из смысловых классов.

## 3.1 Canonical SplitOS data

SplitOS является владельцем истины.

Examples:

```text
SplitOS Account mapping
Entitlement
Committed Operational Mode
Game Profile
Build Manifest
Component Classification
SplitOS Release compatibility decision
```

---

## 3.2 SplitOS policy/configuration

Данные описывают желаемое поведение.

Examples:

```text
Mode Policy
Application Classification
Mode Lifecycle Rule
Shared App Policy
Display / Audio / Input preference
Optimization preference
```

Policy не является доказательством фактического Windows state.

---

## 3.3 External projection

SplitOS хранит собственное представление внешнего состояния, но не становится его authority.

Examples:

```text
Game Installation Projection
External Game Client library projection
External license availability projection
```

Canonical source остаётся внешним.

---

## 3.4 Evidence snapshot

Это интерпретированный снимок фактического окружения, используемый для решений.

Examples:

```text
Hardware Snapshot
Display capability snapshot
Actual process/service state evidence
```

Snapshot может устаревать и должен иметь понятие freshness.

---

## 3.5 Transaction / recovery data

Данные, необходимые для понимания незавершённой операции и восстановления coherent state.

Examples:

```text
Mode Transition Record
Update Transaction Record
Recovery Context
```

---

# 4. Identity & Entitlement

## 4.1 SplitOSAccount

### Meaning

Продуктовая identity пользователя внутри SplitOS.

Это **не**:

```text
Windows Account
Microsoft Account
Steam Account
Epic Account
Xbox Account
```

### Ownership

```text
Product Identity & Entitlement
```

### Relations

```text
SplitOSAccount
├── has Entitlements
├── has User Preferences
├── may own/reference Game Profiles
└── may be associated with one or more SplitOS Installations
```

Последняя cardinality пока не фиксируется как product limitation.

---

## 4.2 Entitlement

### Meaning

Каноническое продуктово-правовое состояние доступа к SplitOS capability/service.

Conceptual kinds may include:

```text
Feature Entitlement
Update Entitlement
Support Entitlement
```

Конкретный pricing/package model не относится к Data layer.

### Important distinction

```text
Payment evidence
        ≠
Entitlement
```

Payment provider в будущем может быть evidence source, но resulting SplitOS entitlement принадлежит SplitOS.

---

# 5. Distribution & Release

## 5.1 SplitOSRelease

### Meaning

Версионированная поддерживаемая комбинация SplitOS-owned product artifacts и compatibility knowledge.

Conceptually relates to:

```text
SplitOSRelease
├── Supported Windows Base decision
├── Build Manifest
├── SplitOS Package Set
├── Component Classification version
└── Compatibility knowledge
```

`SplitOSRelease` не означает, что SplitOS владеет Windows binaries.

---

## 5.2 WindowsBase

### Meaning

Идентичность Windows source/base, относительно которой SplitOS принимает compatibility decision.

Potential identifying characteristics at conceptual level:

```text
edition
architecture
build/version
language where relevant
source identity/integrity evidence
```

### Ownership

Windows implementation/source remains Microsoft-owned.

SplitOS owns only its decision:

```text
WindowsBase X is supported for SplitOSRelease Y
```

---

## 5.3 BuildManifest

### Meaning

Каноническое build-time definition конкретного SplitOS release.

Owner:

```text
Distribution Engineering
```

Contains conceptually:

```text
supported base reference
component actions
SplitOS packages
baseline policies
provisioning rules
recovery/update assets
manifest version
```

---

## 5.4 WindowsComponentDefinition

### Meaning

Идентифицированный Windows component, который SplitOS осознанно учитывает в своем baseline knowledge.

Component type may conceptually be:

```text
SERVICE
DRIVER
APPX
FEATURE
PACKAGE
TASK
POLICY
OTHER
```

---

## 5.5 ComponentClassificationDecision

### Meaning

Версионированное решение SplitOS о lifecycle конкретного Windows component для конкретного supported baseline/release.

Values:

```text
REMOVE
DISABLE
MODE_MANAGED
KEEP
TBD
```

### Relation

```text
WindowsComponentDefinition
        ↓
ComponentClassificationDecision
        ↓
SplitOSRelease / BuildManifest
```

Classification is not an intrinsic eternal property of a Windows component.

Она может измениться между SplitOS releases после тестов.

---

# 6. Installed Baseline

## 6.1 SplitOSInstallation

### Meaning

Конкретная установленная экземплярная среда SplitOS на пользовательском PC.

Она связывает:

```text
Installed SplitOS release
Known Windows base identity
Local baseline state
Local runtime identity/context
```

Это понятие необходимо, чтобы отличать:

```text
"SplitOS Release 1.2 exists"
```

от:

```text
"этот конкретный компьютер установлен/обновлён до SplitOS Release 1.2"
```

---

## 6.2 InstalledBaselineIdentity

### Meaning

Каноническое локальное знание о том, к какому supported build baseline относится установка.

It is not equivalent to actual component health.

Conceptual relation:

```text
SplitOSInstallation
→ InstalledBaselineIdentity
→ SplitOSRelease
```

---

## 6.3 BaselineDriftObservation

### Meaning

Evidence того, что actual installed system composition отличается от expected baseline.

Owner of observation/diagnostic interpretation:

```text
Compatibility / Runtime validation area
```

Drift observation не должен автоматически переписывать expected baseline.

```text
Expected baseline
≠
Observed drift
```

---

# 7. Operational Mode domain

## 7.1 OperationalModeState

### Meaning

Каноническое committed operational state SplitOS.

Values:

```text
NONE
WORK
GAME
```

Owner:

```text
Mode Intent & Active Mode State
```

Rule:

```text
WORK xor GAME
```

when committed operational mode exists.

---

## 7.2 ModeTransitionRecord

### Meaning

Данные текущей или последней управляемой попытки изменения mode context.

Minimum conceptual information:

```text
source mode
target mode
initiator
transition lifecycle state
commit reached or not
terminal outcome
recovery relevance
```

Exact event/journal representation is deferred.

### Why it exists

После crash/reboot SplitOS должен быть способен ответить:

```text
Was transition active?
What was source?
What was target?
Was semantic commit reached?
```

---

# 8. Application domain

## 8.1 Application

### Meaning

Приложение/capability, поведение которого SplitOS может учитывать в mode lifecycle.

SplitOS `Application` is product representation, not ownership of internal application data.

---

## 8.2 ApplicationClassification

### Meaning

SplitOS-owned classification application role.

Canonical classes:

```text
WORK
GAME
GAME_CLIENT
SHARED
SYSTEM
```

Classification answers:

> Как SplitOS интерпретирует роль application в собственных flows?

It does not redefine external product semantics.

---

## 8.3 ApplicationLifecyclePolicy

### Meaning

Policy desired behavior приложения в конкретном mode/context.

Conceptually may determine:

```text
may start
should remain active
should stop
requires confirmation before stop
excluded from automatic management
special presentation behavior
```

---

## 8.4 SharedAppProfile

### Meaning

SplitOS configuration for an application classified as `SHARED` when used in Game Mode.

Possible presentation intent:

```text
Overlay
Locked Window
Secondary Display
Background
```

This is SplitOS presentation/configuration data, not the application's own data.

---

# 9. Gaming domain

## 9.1 GameClient

### Meaning

SplitOS representation of supported external gaming platform/client.

Examples:

```text
Steam
Epic Games
Battle.net
Xbox
```

The external client remains authority for its accounts, licensing, installation and platform-specific business logic.

---

## 9.2 Game

### Meaning

Canonical SplitOS identity of a supported playable title inside the unified Game Launcher domain.

`Game` is not identical to executable path.

One logical game may have:

- external client identity;
- one or more installations over time;
- multiple SplitOS Game Profiles.

---

## 9.3 GameInstallationProjection

### Meaning

SplitOS projection of installation state reported/discovered through a supported Game Client.

Conceptual data can include:

```text
Game
Game Client
external installation identifier
installation location evidence
availability state
last reconciliation time
```

### Critical rule

```text
GameInstallationProjection
≠
canonical external installation truth
```

If Steam/Epic/etc. disagrees with stale SplitOS projection, external authority wins and projection must be reconciled.

---

## 9.4 GameProfile

### Meaning

Каноническая SplitOS configuration игрового сценария для конкретной игры.

One Game:

```text
1 → many GameProfiles
```

Example:

```text
Cyberpunk 2077
├── Desktop / 1440p / 280 Hz / Keyboard & Mouse
└── TV / 4K / 60 Hz / Controller
```

GameProfile conceptually combines references/preferences for:

```text
Target Display Profile
Input Profile
Performance / Optimization intent
Supported game-setting preferences
User Overrides
```

It should not duplicate external platform license/install truth.

---

## 9.5 GameSessionRecord

### Meaning

Runtime interpretation of the currently managed game-session lifecycle.

Conceptually relates to:

```text
Game
GameProfile
GameClient
Game Session State
start/end evidence
launch outcome
```

This is runtime/session data, not the game's save data.

---

# 10. Hardware and device domain

## 10.1 HardwareSnapshot

### Meaning

SplitOS-owned interpreted snapshot built from Windows/driver/device evidence.

Potential content:

```text
CPU
GPU
VRAM
RAM
storage context
connected displays
input devices
relevant capability evidence
```

### Rule

Snapshot is time-bound evidence.

```text
HardwareSnapshot at T1
```

must not be treated as eternal hardware truth at T2.

---

## 10.2 DisplayEndpoint

### Meaning

SplitOS representation of a currently or previously known display endpoint.

Physical/display capabilities remain derived from Windows/driver/device evidence.

---

## 10.3 AudioEndpoint

SplitOS representation of a usable audio output/input endpoint relevant to mode profiles.

---

## 10.4 InputEndpoint

SplitOS representation of keyboard/mouse/controller or other supported input device relevant to navigation/game profile selection.

---

# 11. Profile/configuration concepts

Detailed composition lives in `Configuration Model.md`, but conceptual relationships are:

```text
SplitOSAccount
  └── User Preferences

SplitOSInstallation
  ├── Work Mode Configuration
  └── Game Mode Configuration

Game
  └── GameProfile [1..n]
        ├── Display preference
        ├── Input preference
        ├── Optimization preference
        └── User override intent
```

Mode configuration and Game Profile are not the same object.

---

# 12. Compatibility / Update domain

## 12.1 CompatibilityDecision

### Meaning

Каноническое решение SplitOS о поддержке конкретной комбинации.

Examples:

```text
Windows Base X + SplitOS Release Y → SUPPORTED
Game Client Version A + Integration Version B → SUPPORTED
```

Owner:

```text
Compatibility Management
```

Test evidence supports the decision but is not the decision itself.

---

## 12.2 UpdateTransactionRecord

### Meaning

Runtime/update data about an attempted SplitOS update.

Conceptual information:

```text
source release
target release
compatibility decision consumed
execution state
outcome
rollback/recovery relevance
```

Entitlement and compatibility remain separately owned facts.

---

# 13. Recovery / diagnostics domain

## 13.1 RecoveryContext

### Meaning

Knowledge needed to choose and verify a safe recovery target.

Potential references:

```text
last committed mode
last known safe baseline/release
active/failed transition
failed update
observed drift
```

RecoveryContext does not own the underlying facts; it consumes them to coordinate recovery.

---

## 13.2 DiagnosticRecord

### Meaning

Recorded evidence about SplitOS actions/results for diagnostics/support.

Critical rule:

```text
DiagnosticRecord
≠
canonical business/system truth
```

Logs may describe an entitlement, mode or update result but never become their owner.

---

# 14. High-level conceptual relationships

```text
SplitOSAccount
├── Entitlement
└── User Preferences

SplitOSRelease
├── WindowsBase compatibility
├── BuildManifest
└── ComponentClassificationDecision[*]

SplitOSInstallation
├── InstalledBaselineIdentity
├── OperationalModeState
├── ModeTransitionRecord
├── Mode Configurations
├── HardwareSnapshot[*]
└── GameSessionRecord[*]

GameClient
└── GameInstallationProjection[*]
       └── Game
            └── GameProfile[*]

GameProfile
├── Display preference
├── Input preference
├── Optimization preference
└── User override intent
```

---

# 15. Concepts deliberately NOT merged

The following must remain distinct:

```text
SplitOSAccount
≠ Windows Account

Entitlement
≠ Payment transaction

WindowsBase
≠ SplitOSRelease

BuildManifest
≠ InstalledBaselineIdentity

OperationalModeState
≠ ModeTransitionRecord

GameClient
≠ Game

Game
≠ GameInstallationProjection

GameProfile
≠ HardwareSnapshot

Desired display profile
≠ Actual display state

DiagnosticRecord
≠ Canonical system state
```

These distinctions are essential to prevent competing truth.

---

# 16. Open data questions

The following remain open for later logical/physical modeling:

- account-to-installation cardinality and device identity policy;
- cloud vs local ownership of user profiles;
- offline entitlement cache semantics and expiry;
- stable identity strategy for games across external clients;
- whether the same title installed through two clients is one `Game` with two projections or separate library entries in some cases;
- stable identity strategy for displays after reconnect/driver changes;
- persistence granularity for mode-transition journal;
- retention of game-session history;
- retention/freshness rules for hardware snapshots;
- representation of per-game user overrides versus effective applied config;
- baseline-drift evidence retention;
- diagnostic retention/privacy policy.

---

# 17. Result

The core SplitOS domain is not a set of tables but a set of owned meanings:

```text
Identity
Release/Baseline
Mode
Applications
Games/Profiles
Hardware Evidence
Compatibility
Transactions/Recovery
```

The next logical data step is to define configuration composition and lifecycle without prematurely choosing physical storage.
