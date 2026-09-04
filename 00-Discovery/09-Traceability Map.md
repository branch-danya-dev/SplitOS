# SplitOS — Discovery Traceability Map

## 1. Purpose

Документ связывает discovery-решения с каноническими слоями проекта.

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
| EL-002 | DEC-003 startup selection | System Context | superseded by DEC-036/037/041 |
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
| Product clarification | DEC-035/039/041 | Windows user ↔ SplitOS Account | FR-ACCOUNT / FR-FIRST / Runtime Access Model |
| Product clarification | DEC-036/037/038 | FREE vs PRO runtime access | FR-ACCESS / Runtime Access State / First Run Behavior |
| Product clarification | DEC-040 | Offline/degraded access | FR-ACCESS / Runtime Access State / First Run Behavior |
| Product clarification | DEC-042/043 | Manager + subscription/payment boundary | FR-MANAGER / FR-ENT / Identity & Runtime Access Data |

---

## 3. Current Analysis & Design traceability

```text
03-Analysis-and-Design/
├── 00-Boundaries/
├── 01-Responsibilities/
├── 02-Ownership/
├── 03-States/
├── 04-Behavior/
├── 05-Data/
├── 06-Interfaces/
├── 07-Integrations/
└── 08-Flows/
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
| Product Identity & Entitlement | DEC-033, DEC-035..043 / FR-ENT / FR-ACCOUNT / FR-ACCESS |
| Mode Intent & Active Mode State | DEC-002 / FR-MODE / NFR-REL |
| Mode Transition Coordination | DEC-016/017 / FR-TRANS / NFR-TRANS |
| Mode Policy | DEC-002/031/037 / FR-WORK / FR-GAME / FR-APP |
| Application Lifecycle Policy | DEC-006/016/031 / FR-APP |
| Display / Audio / Input / Power Context | DEC-026 / FR-DISPLAY / FR-AUDIO / FR-INPUT |
| Game Library Representation | DEC-006/009 / FR-GAME / FR-LAUNCHER |
| Game Launch Orchestration | DEC-007/008/037 / FR-LAUNCHER / FR-ACCESS |
| Game Profiles | DEC-011..014 / FR-GAME / FR-OPT |
| Hardware Context Evaluation | DEC-012/013 / FR-HW |
| Game Optimization Policy | DEC-014/015/024 / FR-OPT |
| Game Mode UX | DEC-008/021/037 / FR-LAUNCHER / NFR-UX |
| Shared App Experience | DEC-018/019 / FR-SHARED |
| Compatibility / Update / Recovery | DEC-022/023 / FR-UPDATE / FR-RECOVERY / NFR-UPD |
| Observability & Diagnostics | NFR-OBS / transition/update/recovery requirements |

### 3.3 Ownership

```text
Windows user identity/session
→ Windows authority

SplitOS Account / association / entitlement
→ Product Identity & Entitlement

Managed runtime access decision
→ Product Identity & Entitlement

Payment transaction result
→ external Payment Provider evidence

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

SplitOS Game Profile
→ Game Profiles
```

### 3.4 States

| Requirement / invariant | Canonical state model |
|---|---|
| FREE base experience | Runtime Access State Model → `ManagedRuntime = DISABLED`, `OperationalMode = NONE` |
| PRO managed mode access | Runtime Access State Model → `ManagedRuntime = ENABLED` |
| `WORK xor GAME` | System State + Runtime Access State → only when managed runtime enabled |
| startup / entitlement branching | Runtime Access State Model |
| transactional Work↔Game | Mode Transition Model |
| game exit keeps Game Mode | Game Session State Model |
| one managed foreground game session v1 | Game Session State Model |
| no premature target-mode commit | System State + Mode Transition Models |

### 3.5 Behavior

| Scenario | Canonical behavior artifact | Primary requirements |
|---|---|---|
| First Run / FREE-PRO branching | First Run and Runtime Access Behavior | FR-ACCOUNT / FR-FIRST / FR-ACCESS |
| Startup managed mode path | Startup Behavior + Runtime Access refinement | FR-USER / FR-MODE / NFR-REL |
| Work → Game | Work to Game Behavior | FR-TRANS / FR-APP / NFR-TRANS |
| Game Launch | Game Launch Behavior | FR-LAUNCHER / FR-GAME / FR-HW / FR-OPT |
| Game → Work | Game to Work Behavior | FR-MODE / FR-APP / FR-RECOVERY |

### 3.6 Data

| Meaning / requirement area | Canonical data concept / artifact |
|---|---|
| Windows user context | `WindowsUserContext` as Windows-owned identity evidence |
| SplitOS identity | `SplitOSAccount` |
| Windows user ↔ SplitOS identity | `WindowsUserAccountAssociation` |
| product capability access | `Entitlement` + `ManagedRuntimeAccessDecision` |
| payment result | `PaymentEvidenceProjection` — external evidence |
| supported distribution release | `SplitOSRelease`, `WindowsBase`, `BuildManifest` |
| Windows component lifecycle knowledge | `WindowsComponentDefinition` + `ComponentClassificationDecision` |
| installed release/baseline identity | `SplitOSInstallation` + `InstalledBaselineIdentity` |
| baseline deviation | `BaselineDriftObservation` as evidence, not expected truth |
| managed mode truth | `OperationalModeState` |
| crash-safe transition semantics | `ModeTransitionRecord` |
| application role/lifecycle | `Application`, `ApplicationClassification`, `ApplicationLifecyclePolicy` |
| unified gaming library | `Game`, `GameClient`, `GameInstallationProjection` |
| per-game user scenario | `GameProfile` |
| hardware/device evidence | `HardwareSnapshot`, device endpoint representations |
| release/client compatibility | `CompatibilityDecision` |
| update execution | `UpdateTransactionRecord` |
| recovery coordination | `RecoveryContext` |
| diagnostics | `DiagnosticRecord` — evidence only |

### 3.7 Interfaces

| Requirement / behavior area | Canonical interface family |
|---|---|
| SplitOS Account resolution | `IF-ID-001`, `EXT-ID-001` |
| Entitlement / FREE-PRO runtime access | `IF-ID-002`, `IF-ACCESS-001/002`, `EXT-ID-002/003` |
| Mode selection / committed mode | `IF-MODE-001..003` |
| transactional Work↔Game | `IF-TRANS-001..004` |
| effective Work/Game policy | `IF-POLICY-001` |
| application classification/lifecycle | `IF-APP-001..003`, `EXT-WIN-010` |
| display desired-vs-actual | `IF-DISPLAY-001..003`, `EXT-WIN-DISPLAY-*` |
| audio/input/power context | `IF-AUDIO-*`, `IF-INPUT-*`, `IF-POWER-*` |
| hardware refresh/invalidation | `IF-HW-001/002` |
| game library projection | `IF-LIB-001/002`, `EXT-GC-001/002/003` |
| Game Profile / optimization | `IF-PROFILE-001/002`, `IF-OPT-001` |
| managed game launch | `IF-LAUNCH-001/002`, `EXT-GC-004/005` |
| Shared Apps presentation | `IF-SHARED-001` |
| compatibility / update | `IF-COMPAT-001`, `IF-UPDATE-*`, `EXT-MS-UPDATE-001` |
| recovery | `IF-RECOVERY-001/002` |
| diagnostics | `IF-OBS-001` |
| payment/checkout evidence | `EXT-PAY-001/002` |
| Builder Windows source | `EXT-MS-SOURCE-001` |

### 3.8 Integrations

| Interface / capability | Integration mechanism / status |
|---|---|
| Windows user/session context | WTS / Win32 session APIs — VERIFIED |
| UI/runtime → privileged machine operations | Runtime Host ↔ secured Named Pipe ↔ Privileged Broker — CANDIDATE baseline |
| display topology/capabilities | `QueryDisplayConfig` / `DisplayConfigGetDeviceInfo` — VERIFIED |
| display apply | `SetDisplayConfig` + read-back verification — VERIFIED |
| audio endpoint discovery/events | Core Audio / MMDevice / `IMMNotificationClient` — VERIFIED |
| system default audio switching | supported public mechanism not established — OPEN |
| power plan | PowrProf `PowerGet/SetActiveScheme` — VERIFIED |
| process evidence | Win32 process enumeration / handles — VERIFIED/CANDIDATE |
| service lifecycle | Service Control Manager APIs — VERIFIED |
| Game Client semantics | per-client adapter architecture — CANDIDATE |
| Steam local metadata | version-sensitive best-effort evidence, not canonical public contract |
| Epic/Xbox/Battle.net exact local integration | OPEN/CANDIDATE until validation |
| SplitOS Account | HTTPS backend boundary — CANDIDATE baseline |
| payment | hosted checkout + backend validated provider evidence — CANDIDATE baseline |
| Builder servicing | versioned Build Manifest + DISM/offline servicing where applicable — VERIFIED/CANDIDATE |
| SplitOS/Windows update | compatibility-gated controlled update orchestration — CANDIDATE; exact update technology OPEN |

### 3.9 Flows

| Product scenario | Canonical flow | Key composed layers |
|---|---|---|
| First Windows sign-in / account onboarding | `FL-01A` First Run | Windows user evidence → account association → entitlement → FREE/PRO |
| Normal later sign-in | `FL-01B` | account association → entitlement refresh/offline policy → runtime access |
| FREE → PRO upgrade | `FL-01C` | Manager → backend → hosted checkout → payment evidence → entitlement refresh |
| PRO downgrade / expiry | `FL-01D` | entitlement loss → safe convergence to base Windows experience |
| Explicit Work → Game | `FL-02A` | mode request → blockers → target policy → apply → verify → commit GAME |
| Direct managed game launch from Work | `FL-02B + FL-03` | reuse Work→Game then continue original launch intent |
| Managed game launch | `FL-03A` | library/client projection → hardware/profile → prepare → client handoff → GAME_RUNNING evidence |
| Normal game exit | `FL-03B` | exit evidence → cleanup → return to Game Launcher; mode remains GAME |
| Game → Work | `FL-04A` | inspect game session → resolve Work target → apply → verify → commit WORK |
| Update | `FL-05A..D` | entitlement + compatibility → durable transaction → apply → verify → baseline commit |
| Recovery | `FL-05E` | failure evidence → safe target selection → recovery operations → verification → stable startup |

Canonical flow artifacts:

```text
08-Flows/Flow Model.md
08-Flows/First Run and Subscription Flow.md
08-Flows/Work to Game Flow.md
08-Flows/Managed Game Launch Flow.md
08-Flows/Game to Work Flow.md
08-Flows/Update and Recovery Flow.md
08-Flows/*.mmd
```

Major state mutation flows obey the coordination rule:

```text
Mode Transition
or Update
or Recovery
```

must not independently mutate conflicting machine state at the same time.

---

## 4. Example end-to-end traceability

### FREE vs PRO runtime access

```text
DEC-035..043
→ FR-ACCOUNT / FR-FIRST / FR-ACCESS / FR-MANAGER
→ Product Identity & Entitlement
→ Runtime Access State Model
→ First Run and Runtime Access Behavior
→ WindowsUserAccountAssociation / Entitlement / ManagedRuntimeAccessDecision
→ IF-ID / IF-ACCESS + EXT-ID / EXT-PAY
→ HTTPS Account Backend + hosted checkout/payment evidence
→ FL-01 First Run / Subscription Flow
→ future Failure + Trust + Verification rules
```

### Work XOR Game

```text
DEC-002 + DEC-037
→ FR-MODE / FR-ACCESS
→ Mode Intent & Active Mode State
→ Runtime access gate + Operational Mode
→ Work to Game Behavior / Game to Work Behavior
→ OperationalModeState
→ IF-MODE + IF-TRANS
→ Runtime Host + Windows integrations + Privileged Broker where required
→ FL-02 Work→Game / FL-04 Game→Work
```

### Safe Work → Game transition

```text
DEC-016/017
→ FR-TRANS-* / NFR-TRANS-*
→ Mode Transition Coordination
→ Mode Transition Model
→ Work to Game Behavior
→ ModeTransitionRecord
→ IF-TRANS + IF-POLICY + IF-APP + system-context interfaces
→ process/service/display/device integration mechanisms
→ FL-02 Work to Game Flow
→ future Failure / Verification cases
```

### Managed game launch

```text
DEC-006/007/008/009/011..014
→ FR-GAME / FR-LAUNCHER / FR-HW / FR-OPT
→ Game Library + Profiles + Hardware + Optimization + Launch Orchestration
→ Game / GameClient / GameInstallationProjection / GameProfile / HardwareSnapshot
→ IF-LIB / IF-PROFILE / IF-HW / IF-OPT / IF-LAUNCH
→ per-client Game Client Adapter + Windows evidence
→ FL-03 Managed Game Launch Flow
→ future Game Client failure matrix / Verification
```

### Update / recovery

```text
DEC-022/023
→ FR-UPDATE / FR-RECOVERY / NFR-UPD
→ Compatibility Management + Update Orchestration + Recovery Coordination
→ CompatibilityDecision / UpdateTransactionRecord / RecoveryContext / InstalledBaselineIdentity
→ IF-COMPAT / IF-UPDATE / IF-RECOVERY
→ servicing + Privileged Broker + platform evidence
→ FL-05 Update and Recovery Flow
→ future Failure / Trust / Verification
```

### Build-time Windows preparation

```text
DEC-028..032
→ FR-BUILD-* / NFR-INSTALL-*
→ Distribution Engineering
→ SplitOS Build Pipeline + Component Classification Model
→ SplitOSRelease / BuildManifest / ComponentClassificationDecision
→ EXT-MS-SOURCE-001
→ source validation + manifest executor + DISM/offline servicing
→ future dedicated Builder Specification / Verification
```

---

## 5. Next traceability extensions

Следующий слой продолжает цепочку:

```text
Requirement ID
→ System responsibility
→ Owner
→ State / Behavior
→ Data concept
→ Interface / Contract
→ Integration mechanism
→ End-to-end Flow
→ Failure behavior
→ Trust rule
→ Verification case
```

Трассировка не заменяет сами модели. Её задача — позволить ответить:

> Почему существует это требование, какое решение его породило, кто владеет смыслом, каким механизмом оно исполняется, как выглядит полный flow и где проверяется безопасный результат?