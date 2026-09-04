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
| EL-030 | DEC-028 Windows source distribution model | Distribution / Build Boundary | FR-BUILD / NFR-INSTALL |
| EL-031 | DEC-029 build-time preparation | Distribution Engineering | FR-BUILD / NFR-INSTALL |
| EL-032 | DEC-030 component lifecycle classes | Windows Component Baseline | FR-BUILD / FR-APP |
| EL-033 | DEC-031 mode-managed capability | Work/Game Runtime | FR-BUILD / FR-WORK / FR-GAME |
| EL-034 | DEC-032 build vs runtime responsibility | Runtime Boundary | FR-BUILD / Responsibilities / Ownership |
| EL-035 | DEC-033 account monetization | User/Entitlement | FR-ENT / FR-USER / NFR-SEC |
| EL-036 | DEC-034 pre-install disclosure | Setup UX | FR-SETUP / NFR-UX |
| Product clarification | DEC-035/039/041 | Windows user ↔ SplitOS Account | FR-ACCOUNT / FR-FIRST / Runtime Access Model |
| Product clarification | DEC-036/037/038 | FREE vs PRO runtime access | FR-ACCESS / Runtime Access State / First Run Behavior |
| Product clarification | DEC-040 | Offline/degraded access | FR-ACCESS / Runtime Access / Failure / Trust |
| Product clarification | DEC-042/043 | Manager + subscription/payment boundary | FR-MANAGER / FR-ENT / Identity / Payment Trust |

---

## 3. Current Analysis & Design baseline

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
├── 08-Flows/
├── 09-Failures/
└── 10-Trust/
```

### 3.1 Boundaries

| Artifact | Primary source |
|---|---|
| System Boundary Analysis | DEC-001, DEC-028, DEC-032 |
| SplitOS Build Pipeline | DEC-028, DEC-029, DEC-034 |
| Windows Component Classification Model | DEC-030, DEC-031 |
| Installed Runtime Boundary | DEC-002, DEC-032, DEC-033 |

### 3.2 Responsibilities

| Responsibility | Sources |
|---|---|
| Distribution Engineering | DEC-028..032 / FR-BUILD |
| Product Identity & Entitlement | DEC-033, DEC-035..043 / FR-ENT / FR-ACCOUNT / FR-ACCESS |
| Mode State / Transition / Policy | DEC-002, DEC-016/017, DEC-031 / FR-MODE / FR-TRANS |
| Application Lifecycle | DEC-006/016/031 / FR-APP |
| Display / Audio / Input / Power | DEC-026 / FR-DISPLAY / FR-AUDIO / FR-INPUT |
| Game Library / Launch / Profiles | DEC-006..014 / FR-GAME / FR-LAUNCHER / FR-OPT |
| Compatibility / Update / Recovery | DEC-022/023 / FR-UPDATE / FR-RECOVERY |
| Observability & Diagnostics | NFR-OBS / transition/update/recovery requirements |

### 3.3 Ownership

```text
Windows user identity/session
→ Windows authority

SplitOS Account / association / entitlement
→ Product Identity & Entitlement

Managed runtime access decision
→ Product Identity & Entitlement

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

Game Profile
→ Game Profiles
```

### 3.4 States

| Invariant | Canonical model |
|---|---|
| FREE experience | Runtime Access → `ManagedRuntime=DISABLED`, `OperationalMode=NONE` |
| PRO managed runtime | Runtime Access → `ManagedRuntime=ENABLED` |
| `WORK xor GAME` | System State + Runtime Access when managed runtime enabled |
| transactional Work↔Game | Mode Transition Model |
| game exit keeps Game Mode | Game Session State Model |
| no premature target commit | System State + Mode Transition |

### 3.5 Behavior

| Scenario | Artifact |
|---|---|
| First Run / FREE-PRO | First Run and Runtime Access Behavior |
| Startup | Startup Behavior |
| Work → Game | Work to Game Behavior |
| Game Launch | Game Launch Behavior |
| Game → Work | Game to Work Behavior |

### 3.6 Data

| Meaning | Canonical data |
|---|---|
| SplitOS identity | `SplitOSAccount` |
| Windows user association | `WindowsUserAccountAssociation` |
| capability access | `Entitlement`, `ManagedRuntimeAccessDecision` |
| installed baseline | `SplitOSInstallation`, `InstalledBaselineIdentity` |
| managed mode truth | `OperationalModeState` |
| crash-safe transition | `ModeTransitionRecord` |
| game library projection | `Game`, `GameClient`, `GameInstallationProjection` |
| game configuration | `GameProfile` |
| hardware evidence | `HardwareSnapshot`, endpoint representations |
| compatibility | `CompatibilityDecision` |
| update/recovery | `UpdateTransactionRecord`, `RecoveryContext` |
| diagnostics | `DiagnosticRecord` — evidence only |

### 3.7 Interfaces

| Area | Interface family |
|---|---|
| Account / entitlement | `IF-ID-*`, `IF-ACCESS-*`, `EXT-ID-*` |
| Mode / transition | `IF-MODE-*`, `IF-TRANS-*`, `IF-POLICY-*` |
| Application lifecycle | `IF-APP-*`, `EXT-WIN-010` |
| Display/audio/input/power | corresponding `IF-*` + Windows external boundaries |
| Game library/profile/launch | `IF-LIB-*`, `IF-PROFILE-*`, `IF-LAUNCH-*`, `EXT-GC-*` |
| Compatibility/update/recovery | `IF-COMPAT-*`, `IF-UPDATE-*`, `IF-RECOVERY-*` |
| Payment / Windows source | `EXT-PAY-*`, `EXT-MS-*` |

### 3.8 Integrations

| Capability | Mechanism / status |
|---|---|
| Windows user/session | WTS / Win32 — VERIFIED |
| privileged local operations | Runtime Host ↔ secured local IPC ↔ Privileged Broker — CANDIDATE |
| display read/apply | CCD APIs + read-back verification — VERIFIED |
| audio discovery/events | Core Audio/MMDevice — VERIFIED |
| default audio switching | OPEN |
| power schemes | PowrProf — VERIFIED |
| process/service evidence | Win32 / SCM — VERIFIED/CANDIDATE |
| Game Clients | capability-based per-client adapters — CANDIDATE |
| Account backend | HTTPS boundary — CANDIDATE |
| payment | hosted checkout + backend validation — CANDIDATE |
| Builder | manifest executor + supported offline servicing — VERIFIED/CANDIDATE |
| Update | compatibility-gated orchestration; exact technology OPEN |

### 3.9 Flows

| Scenario | Canonical flow |
|---|---|
| First Run / account / FREE-PRO | `FL-01` |
| Work → Game | `FL-02` |
| Managed Game Launch / Exit | `FL-03` |
| Game → Work | `FL-04` |
| Update / Recovery | `FL-05` |

Major machine mutation rule:

```text
Mode Transition
or Update
or Recovery
```

must not independently execute conflicting mutations simultaneously.

### 3.10 Failures

Canonical artifacts:

```text
09-Failures/Failure Model.md
09-Failures/Runtime Failure Scenarios.md
09-Failures/Update Recovery Failure Scenarios.md
09-Failures/Failure Handling Matrix.md
09-Failures/failure-map.mmd
```

Critical safe-convergence rules:

| Failure area | Canonical result |
|---|---|
| Account backend unavailable | Windows remains usable; offline/degraded policy applies |
| Runtime Host crash mid-transition | source committed mode remains canonical unless durable target commit exists |
| Broker unavailable | privileged mutation blocked; no false success |
| Display target not reached | target mode commit prohibited unless fallback resolved + verified |
| Partial mode policy application | mandatory failure → rollback/recovery |
| Game Client auth required | controlled outcome; remain GAME/Launcher |
| Handoff accepted but no game evidence | launch fails; never promote `GAME_RUNNING` |
| Update incomplete | target baseline not committed |
| Reboot/power loss during update | resume/reconcile durable transaction |
| Recovery verification fails | recovery remains incomplete/manual escalation possible |
| SplitOS runtime unrecoverable but Windows works | prioritize base Windows usability |

Canonical safety priority:

```text
User data integrity
→ Windows bootability/base usability
→ known coherent state
→ correct SplitOS canonical state
→ managed runtime restoration
→ UX convenience
```

### 3.11 Trust

Canonical artifacts:

```text
10-Trust/Trust Model.md
10-Trust/Local Privilege and IPC Trust.md
10-Trust/Identity Entitlement and Secret Trust.md
10-Trust/Artifact Build and Update Trust.md
10-Trust/External Evidence Trust.md
10-Trust/Security Control Matrix.md
10-Trust/trust-map.mmd
```

Core trust chain:

```text
Claim / Request
→ identity / issuer
→ integrity
→ freshness
→ context binding
→ capability authorization
→ semantic owner decision
→ sensitive operation
→ actual-state verification
```

Key trust mappings:

| Area | Trust rule / candidate |
|---|---|
| Runtime → Privileged Broker | explicit Named Pipe ACL + caller token/session validation + bounded capability protocol |
| Broker operation surface | no arbitrary command/script/raw admin API |
| SplitOS Account login | external browser native-app flow + PKCE candidate |
| reusable account tokens | Windows-protected storage; DPAPI current candidate |
| FREE/PRO | backend or bounded verifiable offline entitlement evidence; never editable local flag |
| offline PRO | signed/authentic server-issued assertion + expiry/context binding; exact format OPEN |
| Payment | provider→backend authenticated evidence; desktop callback only triggers refresh |
| SplitOS binaries | Authenticode/WinVerifyTrust candidate + protected installation ACL |
| Build/Update Manifest | signed/versioned metadata required; exact envelope OPEN |
| Update packages | digest/signature binding + protected staging + revalidation |
| downgrade | valid old signature alone insufficient; explicit authorized recovery policy required |
| Game Client metadata | bounded adapter/parser; never privileged command input |
| browser/custom URI | untrusted until transaction correlation/server validation |
| Windows/device evidence | trusted only for platform facts it actually proves |
| local Administrator/kernel threat | explicitly outside v1 guarantee |

Trust invariants:

```text
trusted for one capability
!= globally trusted

signed binary
!= authorized request

browser callback
!= entitlement/payment authority

HTTPS download
!= trusted release artifact

external metadata
!= privileged command

cannot prove premium authorization
→ do not grant premium capability
→ preserve base Windows usability
```

---

## 4. End-to-end traceability examples

### FREE vs PRO runtime access

```text
DEC-035..043
→ FR-ACCOUNT / FR-FIRST / FR-ACCESS / FR-MANAGER
→ Product Identity & Entitlement
→ Runtime Access State
→ First Run behavior
→ Entitlement / ManagedRuntimeAccessDecision
→ IF-ID / IF-ACCESS / EXT-ID / EXT-PAY
→ Account Backend + hosted checkout
→ FL-01
→ RF-01 / RF-02
→ Identity Entitlement and Secret Trust
→ future Verification cases
```

### Safe Work → Game

```text
DEC-016/017
→ FR-TRANS / NFR-TRANS
→ Mode Transition Coordination
→ Mode Transition Model
→ Work to Game Behavior
→ ModeTransitionRecord
→ IF-TRANS / IF-POLICY / IF-APP
→ Windows/process/service/display integrations
→ FL-02
→ RF-20..26
→ Local Privilege and IPC Trust + External Evidence Trust
→ future Verification
```

### Managed game launch

```text
DEC-006..014
→ FR-GAME / FR-LAUNCHER / FR-HW / FR-OPT
→ Game Library + Profiles + Launch Orchestration
→ Game / GameClient / GameInstallationProjection / GameProfile
→ IF-LIB / IF-PROFILE / IF-LAUNCH / EXT-GC
→ per-client Game Client Adapter + Windows evidence
→ FL-03
→ RF-30..36
→ External Evidence Trust
→ future Verification
```

### Update / recovery

```text
DEC-022/023
→ FR-UPDATE / FR-RECOVERY / NFR-UPD
→ Compatibility + Update + Recovery
→ CompatibilityDecision / UpdateTransactionRecord / RecoveryContext / InstalledBaselineIdentity
→ IF-COMPAT / IF-UPDATE / IF-RECOVERY
→ servicing / Broker / platform evidence
→ FL-05
→ UF-* / RC-*
→ Artifact Build and Update Trust + Local Privilege Trust
→ future Verification
```

### Build-time Windows preparation

```text
DEC-028..032
→ FR-BUILD / NFR-INSTALL
→ Distribution Engineering
→ SplitOS Build Pipeline / Component Classification
→ BuildManifest / ComponentClassificationDecision
→ EXT-MS-SOURCE
→ source validation + manifest executor + servicing
→ Artifact Build and Update Trust
→ future Builder Specification / Verification
```

---

## 5. Next traceability extension

```text
Requirement
→ Responsibility
→ Owner
→ State / Behavior
→ Data
→ Interface
→ Integration
→ Flow
→ Failure behavior
→ Trust rule
→ Synthesis component
→ Verification case
```

Следующий layer — `11-Synthesis`, после которого A&D baseline можно переводить в detailed Specification.