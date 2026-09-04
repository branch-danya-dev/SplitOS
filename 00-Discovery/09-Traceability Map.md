# SplitOS — Discovery Traceability Map

## 1. Purpose

Документ связывает discovery-решения с каноническими слоями проекта.

Направление трассировки:

```text
Initial Request
      ↓
Problem / Elicitation
      ↓
Decision
      ↓
Concept
      ↓
Functional / Non-Functional Requirement
      ↓
Analysis & Design
      ↓
Specification / Verification
```

---

## 2. High-level traceability

| Discovery source | Decision | Canonical area | Requirement / Analysis family |
|---|---|---|---|
| EL-001 | DEC-002 Work XOR Game | System Context / Concept | FR-MODE / NFR-REL |
| EL-002 | DEC-003 startup selection | System Context | FR-SETUP / NFR-REL |
| EL-003 | DEC-004/005 remain in Game | Game Mode concept | FR-MODE / NFR-REL |
| EL-004 | DEC-006 GAME != GAME_CLIENT | Application model | FR-APP / FR-GAME |
| EL-005 | DEC-007 launch orchestration | Game Launcher | FR-MODE / FR-LAUNCHER |
| EL-008 | DEC-011 multiple game profiles | Game Profiles | FR-GAME / NFR-REL |
| EL-009 | DEC-012/013 hardware validation | Hardware Detection | FR-GAME / NFR-PERF |
| EL-010..012 | DEC-014 optimization objective | Game Experience Optimization | FR-OPT / NFR-PERF |
| EL-014..015 | DEC-016/017 safe transition | Recovery / Mode Transition | FR-RECOVERY / NFR-TRANS |
| EL-016..017 | DEC-018/019 Shared Apps | Game Mode UX | FR-APP / NFR-PERF / NFR-UX |
| EL-018..019 | DEC-001 distribution model | System Context | FR-DIST / NFR-INSTALL |
| EL-020..021 | DEC-022/023 update lifecycle | Windows Update policy | FR-UPDATE / NFR-UPD |
| EL-022 | DEC-008 Game Launcher | Game Launcher | FR-LAUNCHER / NFR-UX |
| EL-023..024 | DEC-014/024 game config boundary | Game Experience Management | FR-OPT / NFR-SEC |
| EL-025..026 | DEC-020/021 UX priority | MVP scope | NFR-UX |
| EL-027 | DEC-026 displays | Display Management | FR-DISPLAY / NFR-COMP |
| EL-028 | DEC-025 one drive | Storage | FR-STORAGE |
| EL-029 | DEC-027 future ecosystem | Extensibility | FR-FUTURE / NFR-EXT |
| EL-030 | DEC-028 Windows source distribution model | Distribution / Build Boundary | FR-BUILD / NFR-INSTALL / Build Pipeline |
| EL-031 | DEC-029 build-time preparation | Distribution Engineering | FR-BUILD / NFR-INSTALL / Build Pipeline |
| EL-032 | DEC-030 component lifecycle classes | Windows Component Baseline | FR-BUILD / FR-APP / Component Classification Model |
| EL-033 | DEC-031 mode-managed capability | Work/Game Runtime | FR-BUILD / FR-WORK / FR-GAME / Installed Runtime Boundary |
| EL-034 | DEC-032 build vs runtime responsibility | Runtime Boundary | FR-BUILD / Responsibilities / Ownership |
| EL-035 | DEC-033 account monetization | User/Entitlement | FR-ENT / FR-USER / NFR-SEC |
| EL-036 | DEC-034 pre-install disclosure | Setup UX | FR-SETUP / NFR-UX |

---

## 3. Current Analysis & Design traceability

Текущий A&D baseline:

```text
03-Analysis-and-Design/
├── 00-Boundaries/
├── 01-Responsibilities/
├── 02-Ownership/
├── 03-States/
├── 04-Behavior/
└── 05-Data/
```

### 3.1 Boundaries

| Analysis artifact | Primary source decisions |
|---|---|
| System Boundary Analysis | DEC-001, DEC-028, DEC-032 |
| SplitOS Build Pipeline | DEC-028, DEC-029, DEC-034 |
| Windows Component Classification Model | DEC-030, DEC-031 |
| Installed Runtime Boundary | DEC-002, DEC-032, DEC-033 |
| system-boundary.mmd | synthesis of build/runtime/external ownership boundaries |

### 3.2 Responsibilities

| Responsibility area | Primary requirement/decision sources |
|---|---|
| Distribution Engineering | DEC-028..032 / FR-BUILD / NFR-INSTALL |
| Product Identity & Entitlement | DEC-033 / FR-ENT / FR-USER |
| Mode Intent & Active Mode State | DEC-002/003 / FR-MODE / NFR-REL |
| Mode Transition Coordination | DEC-016/017 / FR-TRANS / NFR-TRANS |
| Mode Policy | DEC-002/031 / FR-WORK / FR-GAME / FR-APP |
| Application Lifecycle Policy | DEC-006/016/031 / FR-APP |
| Display / Audio / Input / Power Context | DEC-026 / FR-DISPLAY / FR-AUDIO / FR-INPUT |
| Game Library Representation | DEC-006/009 / FR-GAME / FR-LAUNCHER |
| Game Launch Orchestration | DEC-007/008 / FR-LAUNCHER |
| Game Profiles | DEC-011..014 / FR-GAME / FR-OPT |
| Hardware Context Evaluation | DEC-012/013 / FR-HW |
| Game Optimization Policy | DEC-014/015/024 / FR-OPT |
| Game Mode UX | DEC-008/021 / FR-LAUNCHER / NFR-UX |
| Shared App Experience | DEC-018/019 / FR-SHARED |
| Compatibility / Update / Recovery | DEC-022/023 / FR-UPDATE / FR-RECOVERY / NFR-UPD |
| Observability & Diagnostics | NFR-OBS / transition/update/recovery requirements |

### 3.3 Ownership

Ключевые canonical ownership mappings:

```text
User intent
→ User authority

Committed operational mode
→ Mode Intent & Active Mode State

Transition lifecycle/result
→ Mode Transition Coordination

Desired Work/Game state
→ Mode Policy

Actual Windows/device state
→ Windows / Driver / Device evidence authority

External game installation/license truth
→ External Game Client / Platform authority

SplitOS unified game representation
→ Game Library Representation

SplitOS Game Profile
→ Game Profiles

Hardware snapshot interpretation
→ Hardware Context Evaluation

Compatibility decision
→ Compatibility Management

Entitlement state
→ Product Identity & Entitlement

Update execution/result
→ Update Orchestration
```

### 3.4 States

| Requirement / invariant | Canonical state model |
|---|---|
| `WORK xor GAME` | System State Model → Operational Mode |
| startup mode selection | System State Model → Session Lifecycle / `MODE_SELECTION` |
| transactional Work↔Game | Mode Transition Model |
| transition outcomes | Mode Transition Model → `COMPLETED / CANCELLED / FAILED_WITH_SAFE_FALLBACK` |
| game exit keeps Game Mode | Game Session State Model |
| one managed foreground game session v1 | Game Session State Model |
| no premature target-mode commit | System State + Mode Transition Models |

### 3.5 Behavior

| Scenario | Canonical behavior artifact | Primary requirements |
|---|---|---|
| Startup | Startup Behavior | FR-USER, FR-MODE, FR-SETUP, NFR-REL |
| Work → Game | Work to Game Behavior | FR-TRANS, FR-APP, NFR-TRANS |
| Game Launch | Game Launch Behavior | FR-LAUNCHER, FR-GAME, FR-HW, FR-OPT |
| Game → Work | Game to Work Behavior | FR-MODE, FR-APP, FR-RECOVERY |

### 3.6 Data

| Meaning / requirement area | Canonical data concept / artifact |
|---|---|
| SplitOS identity | `SplitOSAccount` / Domain Model |
| product capability access | `Entitlement` / Domain Model + Data Ownership and Lifecycle |
| supported distribution release | `SplitOSRelease`, `WindowsBase`, `BuildManifest` |
| Windows component lifecycle knowledge | `WindowsComponentDefinition` + `ComponentClassificationDecision` |
| installed release/baseline identity | `SplitOSInstallation` + `InstalledBaselineIdentity` |
| baseline deviation | `BaselineDriftObservation` as evidence, not expected truth |
| `WORK xor GAME` canonical runtime truth | `OperationalModeState` |
| crash-safe transition semantics | `ModeTransitionRecord` |
| application role/lifecycle | `Application`, `ApplicationClassification`, `ApplicationLifecyclePolicy` |
| unified gaming library | `Game`, `GameClient`, `GameInstallationProjection` |
| per-game user scenario | `GameProfile` |
| hardware/device evidence | `HardwareSnapshot`, device endpoint representations |
| release/client compatibility | `CompatibilityDecision` |
| update execution | `UpdateTransactionRecord` |
| recovery coordination | `RecoveryContext` |
| diagnostics | `DiagnosticRecord` — evidence only |

Data configuration resolution is defined in:

```text
05-Data/Configuration Model.md
```

Data ownership, copies, freshness and lifecycle are defined in:

```text
05-Data/Data Ownership and Lifecycle.md
```

---

## 4. Example end-to-end traceability

### Work XOR Game

```text
EL-001
→ DEC-002
→ FR-MODE-003 / NFR-REL-001
→ Mode Intent & Active Mode State responsibility
→ canonical owner: Mode Intent & Active Mode State
→ System State Model / Operational Mode
→ Work to Game Behavior / Game to Work Behavior
→ OperationalModeState data concept
```

### Safe Work → Game transition

```text
EL-014..015
→ DEC-016/017
→ FR-TRANS-* / NFR-TRANS-*
→ Mode Transition Coordination
→ Mode Transition ownership
→ Mode Transition Model
→ Work to Game Behavior
→ ModeTransitionRecord
→ future Interfaces / Failures / Verification
```

### Build-time Windows preparation

```text
EL-030..034
→ DEC-028..032
→ FR-BUILD-* / NFR-INSTALL-*
→ Distribution Engineering
→ Build/Runtime ownership split
→ SplitOS Build Pipeline + Component Classification Model
→ SplitOSRelease / BuildManifest / ComponentClassificationDecision
→ future Component Matrix / Specification / Verification
```

### SplitOS entitlement

```text
EL-035..036
→ DEC-033/034
→ FR-ENT-* / FR-SETUP-008..010
→ Product Identity & Entitlement
→ entitlement ownership
→ SplitOSAccount + Entitlement
→ future Trust / Interfaces
```

### Game library and profiles

```text
DEC-006/009/011
→ FR-GAME / FR-LAUNCHER / FR-HW
→ Game Library Representation + Game Profiles
→ external installation authority separated from SplitOS profile authority
→ Game / GameClient / GameInstallationProjection / GameProfile
→ future Game Client interfaces/integrations
```

---

## 5. Next traceability extensions

Следующие слои должны продолжить цепочку:

```text
Requirement ID
→ System responsibility
→ Owner
→ State / Behavior
→ Data concept
→ Interface / Contract
→ Integration / Flow
→ Failure behavior
→ Trust rule
→ Verification case
```

Трассировка не заменяет сами модели. Её задача — позволить ответить:

> Почему существует это требование, какое решение его породило, какая аналитическая модель его объясняет и где проверяется его реализация?
