# SplitOS — Discovery Traceability Map

## 1. Purpose

Документ связывает discovery-решения с каноническими слоями проекта.

```text
Initial Request
→ Problem / Elicitation
→ Decision
→ Concept
→ Functional / Non-Functional Requirement
→ Analysis & Design
→ Synthesis component
→ Specification / Verification
```

---

## 2. High-level traceability

| Discovery source | Decision | Canonical area | Requirement / Analysis family |
|---|---|---|---|
| EL-001 | DEC-002 Work XOR Game | System Context / Concept | FR-MODE / NFR-REL |
| EL-002 | DEC-003 startup selection | System Context | superseded by DEC-036/037/041 |
| EL-003 | DEC-004/005 remain in Game | Game Mode | FR-MODE / NFR-REL |
| EL-004 | DEC-006 GAME != GAME_CLIENT | Application model | FR-APP / FR-GAME |
| EL-005 | DEC-007 launch orchestration | Game Launcher | FR-MODE / FR-LAUNCHER |
| EL-008 | DEC-011 multiple profiles | Game Profiles | FR-GAME / NFR-REL |
| EL-009..012 | DEC-012/013/014 | Hardware / optimization | FR-HW / FR-OPT / NFR-PERF |
| EL-014..015 | DEC-016/017 safe transition | Mode Transition / Recovery | FR-TRANS / FR-RECOVERY / NFR-TRANS |
| EL-016..017 | DEC-018/019 Shared Apps | Game UX | FR-SHARED / NFR-UX |
| EL-018..019 | DEC-001 distribution | System Context | FR-DIST / NFR-INSTALL |
| EL-020..021 | DEC-022/023 update lifecycle | Update/Recovery | FR-UPDATE / NFR-UPD |
| EL-022 | DEC-008 Game Launcher | Game Launcher | FR-LAUNCHER / NFR-UX |
| EL-023..024 | DEC-014/024 config boundary | Game Optimization | FR-OPT / NFR-SEC |
| EL-025..026 | DEC-020/021 Game UX priority | MVP | NFR-UX |
| EL-027 | DEC-026 displays | Display Context | FR-DISPLAY / NFR-COMP |
| EL-028 | DEC-025 storage | Storage | FR-STORAGE |
| EL-029 | DEC-027 future ecosystem | Extensibility | FR-FUTURE / NFR-EXT |
| EL-030 | DEC-028 Windows source model | Build Boundary | FR-BUILD / NFR-INSTALL |
| EL-031 | DEC-029 build-time preparation | Distribution Engineering | FR-BUILD / NFR-INSTALL |
| EL-032 | DEC-030 lifecycle classes | Windows Component Baseline | FR-BUILD / FR-APP |
| EL-033 | DEC-031 MODE_MANAGED | Work/Game Runtime | FR-BUILD / FR-WORK / FR-GAME |
| EL-034 | DEC-032 build/runtime split | Runtime Boundary | FR-BUILD / Responsibilities / Ownership |
| EL-035 | DEC-033 monetization | Identity/Entitlement | FR-ENT / FR-USER / NFR-SEC |
| EL-036 | DEC-034 pre-install disclosure | Setup UX | FR-SETUP / NFR-UX |
| Product clarification | DEC-035/039/041 | Windows user ↔ SplitOS Account | FR-ACCOUNT / FR-FIRST |
| Product clarification | DEC-036/037/038 | FREE vs PRO | FR-ACCESS / Runtime Access |
| Product clarification | DEC-040 | offline/degraded access | FR-ACCESS / Failure / Trust |
| Product clarification | DEC-042/043 | Manager/payment boundary | FR-MANAGER / FR-ENT / Trust |
| Product clarification | DEC-044/047 | independent SplitOS update channel + Windows servicing separation | Update/Compatibility | FR-UPDATE-010..017 / SPEC-11 |
| Product clarification | DEC-045/046 | previous-release local recovery + user-data-preserving rollback | Recovery/Data | FR-RECOVERY-008..016 / SPEC-11 |

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
├── 10-Trust/
└── 11-Synthesis/
```

All layers are READY after merge of `11-Synthesis`.

---

## 4. Layer traceability summary

### 4.1 Boundaries

| Artifact | Primary source |
|---|---|
| System Boundary Analysis | DEC-001, DEC-028, DEC-032 |
| SplitOS Build Pipeline | DEC-028, DEC-029, DEC-034 |
| Windows Component Classification Model | DEC-030, DEC-031 |
| Installed Runtime Boundary | DEC-002, DEC-032, DEC-033 |

### 4.2 Responsibilities / Ownership

| Responsibility | Canonical owner/fact |
|---|---|
| Product Identity & Entitlement | SplitOS Account / Entitlement / Runtime Access |
| Mode State | committed OperationalModeState |
| Mode Transition | ModeTransitionRecord / transition result |
| Mode Policy | target semantic Work/Game policy |
| Application Lifecycle | app classification/lifecycle rules |
| Display/Audio/Input/Power | desired SplitOS context; Windows/device remains actual evidence authority |
| Game Library | SplitOS projection; external clients own install/license truth |
| Game Profiles | GameProfile |
| Hardware | interpreted HardwareSnapshot |
| Game Launch | managed launch transaction/session |
| Compatibility | CompatibilityDecision |
| Update | UpdateTransactionRecord |
| Recovery | RecoveryContext/result |
| Observability | DiagnosticRecord as evidence only |

### 4.3 States / Behavior

| Invariant/scenario | Canonical model |
|---|---|
| FREE experience | ManagedRuntime=DISABLED, OperationalMode=NONE |
| PRO experience | ManagedRuntime=ENABLED, WORK xor GAME |
| Work→Game | Mode Transition Model + Work to Game Behavior |
| Managed launch | Game Session Model + Game Launch Behavior |
| Game exit | returns Launcher, committed GAME stays |
| Game→Work | Game to Work Behavior |
| startup/account | Runtime Access State + First Run/Startup Behavior |

### 4.4 Data

| Meaning | Canonical concept |
|---|---|
| SplitOS identity | SplitOSAccount |
| user association | WindowsUserAccountAssociation |
| entitlement/access | Entitlement / ManagedRuntimeAccessDecision |
| installed baseline | SplitOSInstallation / InstalledBaselineIdentity |
| mode truth | OperationalModeState |
| transition durability | ModeTransitionRecord |
| game library | Game / GameClient / GameInstallationProjection |
| game scenario | GameProfile |
| hardware evidence | HardwareSnapshot / endpoints |
| update/recovery | UpdateTransactionRecord / RecoveryContext |
| diagnostics | DiagnosticRecord |

### 4.5 Interfaces / Integrations

| Area | Contract / mechanism family |
|---|---|
| Account/access | IF-ID / IF-ACCESS / HTTPS backend |
| Mode/transition | IF-MODE / IF-TRANS / Runtime Host orchestration |
| App lifecycle | IF-APP / process-window evidence |
| Display | IF-DISPLAY / CCD APIs + read-back |
| Audio | IF-AUDIO / Core Audio/MMDevice; default switching OPEN |
| Power | IF-POWER / PowrProf |
| Game clients | IF-LIB / IF-LAUNCH / per-client adapters |
| Privileged mutation | bounded IPC → Privileged Broker |
| Update/recovery | IF-UPDATE / IF-RECOVERY / Broker + servicing |
| Builder | Windows source validation + manifest executor + supported servicing |

### 4.6 Flows

```text
FL-01 First Run / FREE-PRO / Upgrade
FL-02 Work → Game
FL-03 Managed Game Launch / Exit
FL-04 Game → Work
FL-05 Update / Recovery
```

Direct managed launch from Work = `FL-02 + FL-03`.

Major conflicting mutation families:

```text
Mode Transition
or Update
or Recovery
```

must be coordinated.

### 4.7 Failures

Core safe-convergence rules:

```text
technical operation success != semantic success
partial application != target commit
verification failure → target commit prohibited
rollback/recovery require verification
backend failure != Windows login failure
GAME_CLIENT auth required != system crash
```

Safety priority:

```text
User data
→ Windows bootability/base usability
→ coherent state
→ correct SplitOS canonical state
→ managed runtime restoration
→ UX convenience
```

### 4.8 Trust

Core trust chain:

```text
Claim/Request
→ Identity/Issuer
→ Integrity
→ Freshness
→ Context binding
→ Capability authorization
→ Semantic owner decision
→ Sensitive operation
→ Verification
```

Key controls/candidates:

- Runtime Host → Broker: explicit ACL/caller-session validation + bounded capability protocol;
- no generic arbitrary admin command contract;
- account auth: external browser + PKCE candidate;
- reusable user token protection: DPAPI candidate;
- entitlement: backend or bounded verifiable offline assertion;
- payment callback never grants entitlement directly;
- binaries/artifacts/manifests require provenance/integrity validation;
- valid old signature does not automatically authorize downgrade;
- external metadata cannot become direct privileged command input.

---

## 5. Synthesis component mapping

### 5.1 Product identity / FREE-PRO

```text
DEC-035..043
→ FR-ACCOUNT / FR-FIRST / FR-ACCESS / FR-MANAGER
→ Product Identity & Entitlement
→ Runtime Access State / First Run Behavior
→ Entitlement + ManagedRuntimeAccessDecision
→ IF-ID / IF-ACCESS
→ Account Backend
→ FL-01
→ account/access failures
→ Identity/Entitlement Trust
→ COMP-UX-01/02 Manager + First Run
→ COMP-RT-01 Runtime Access Coordinator
→ COMP-BE-01..05 Account/Auth/Entitlement Backend
```

### 5.2 Work → Game

```text
DEC-002 + DEC-016/017 + DEC-031
→ FR-MODE / FR-TRANS / FR-APP
→ Mode State / Transition / Policy / Application Lifecycle
→ OperationalModeState + ModeTransitionRecord
→ IF-MODE / IF-TRANS / IF-POLICY / IF-APP
→ Windows adapters + Broker
→ FL-02
→ RF-20..26
→ Local Privilege + External Evidence Trust
→ COMP-RT-02..07 + COMP-PRIV-01
```

### 5.3 Managed game launch

```text
DEC-006..014
→ FR-GAME / FR-LAUNCHER / FR-HW / FR-OPT
→ Game Library / Profiles / Hardware / Optimization / Launch
→ Game + GameClient + GameInstallationProjection + GameProfile + HardwareSnapshot
→ IF-LIB / IF-PROFILE / IF-HW / IF-OPT / IF-LAUNCH
→ per-client adapters + Windows evidence
→ FL-03
→ RF-30..36
→ External Evidence Trust
→ COMP-UX-03 + COMP-RT-07..13 + COMP-ADP-08..12
```

### 5.4 Game → Work

```text
FR-MODE / FR-APP / FR-RECOVERY
→ Game Session + Mode Transition + App Lifecycle
→ FL-04
→ runtime failure rules
→ COMP-RT-02..06 + COMP-RT-12
```

### 5.5 Update / Recovery

```text
DEC-022/023 + DEC-044..047
→ FR-UPDATE / FR-RECOVERY / NFR-UPD
→ Compatibility + Update + Recovery
→ CompatibilityDecision + UpdateTransactionRecord + RecoveryContext + InstalledBaselineIdentity
→ IF-COMPAT / IF-UPDATE / IF-RECOVERY
→ independent SplitOS signed update channel
→ Microsoft-serviced Windows patch lane after SplitOS compatibility approval
→ mandatory previous-release Recovery Capsule
→ user-data-preserving rollback contract
→ FL-05
→ update/recovery failure rules
→ Artifact/Build/Update Trust
→ SPEC-11 Update & Recovery
→ COMP-RT-14..18 + COMP-PRIV-01/02 + COMP-REL-*
```

### 5.6 Build-time Windows preparation

```text
DEC-028..032
→ FR-BUILD / NFR-INSTALL
→ Distribution Engineering
→ BuildManifest + ComponentClassificationDecision
→ source/build external contracts
→ source validation + manifest executor + servicing
→ Artifact Build Trust
→ COMP-BLD-01..04 + COMP-REL-*
```

---

## 6. Final synthesis artifacts

```text
11-Synthesis/System Synthesis.md
11-Synthesis/Logical Component Architecture.md
11-Synthesis/Deployment and Process Topology.md
11-Synthesis/Data and State Placement.md
11-Synthesis/Architecture Baseline Matrix.md
11-Synthesis/Specification Handoff.md
11-Synthesis/system-architecture.mmd
11-Synthesis/deployment-topology.mmd
```

---

## 7. Specification traceability extension

The A&D chain is complete through Synthesis:

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
```

Current update/recovery extension:

```text
DEC-044..047
→ FR-UPDATE-010..017 / FR-RECOVERY-008..016
→ SPEC-11 Update & Recovery
→ implementation units
→ SPEC-14 verification/acceptance cases
```

Remaining OPEN engineering decisions must be attached to their owning specification/research item instead of being silently resolved in code.
