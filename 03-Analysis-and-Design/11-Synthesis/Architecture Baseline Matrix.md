# SplitOS — Architecture Baseline Matrix

## 1. Purpose

Этот документ даёт компактное сведение конечной архитектуры по компонентам.

Он предназначен как bridge в detailed Specification и implementation planning.

---

## 2. Runtime components

| Component | Primary responsibility | Canonical data/state | Main contracts | Trust/privilege | Deployment |
|---|---|---|---|---|---|
| SplitOS Manager | account/configuration/update/recovery UX | none directly | account/access/profile/update/recovery queries & requests | normal user; unprivileged | interactive session |
| First Run Experience | onboarding UX composition | onboarding completion / association request | account sign-in/create, entitlement resolution | normal user; browser callbacks untrusted until validated | Manager surface or companion interactive UI |
| Game Launcher | Game Mode UX, launch requests | none directly | library/profile/launch/session/mode requests | normal user; no privileged authority | interactive session |
| Runtime Access Coordinator | FREE/PRO managed runtime decision | ManagedRuntimeAccessDecision | account/entitlement/access | backend evidence scoped; fail closed for premium | Runtime Host |
| Operational Mode Coordinator | committed mode semantics | OperationalModeState | mode request/read/commit event | semantic owner only | Runtime Host |
| Mode Transition Coordinator | transition transaction | ModeTransitionRecord | blocker/policy/context/apply/verify | source remains canonical until commit | Runtime Host |
| Mode Policy Resolver | effective Work/Game target | ModePolicy/effective target | policy resolve | no direct privileged execution | Runtime Host |
| Application Lifecycle Coordinator | WORK/GAME/CLIENT/SHARED/SYSTEM behavior | app classification/lifecycle policy | process/app evidence + lifecycle requests | external process evidence bounded | Runtime Host |
| System Context Coordinators | display/audio/input/power desired state | desired context | context resolve/apply/verify | actual Windows/device evidence external | Runtime Host |
| Hardware Context Evaluator | interpreted hardware evidence | HardwareSnapshot | hardware refresh/invalidation | freshness-bound evidence | Runtime Host |
| Game Library Projection | unified game library | Game/GameClient/GameInstallationProjection | client discovery/library/evidence | external client authority preserved | Runtime Host |
| Game Profile Resolver | per-game scenario/configuration | GameProfile | profile resolve/update | user/product-owned | Runtime Host |
| Optimization Policy Engine | recommendation/effective optimization intent | OptimizationRecommendation | profile/hardware/compatibility query | no anti-cheat/gameplay cheating authority | Runtime Host |
| Game Launch Orchestrator | managed launch transaction | GameSession/launch transaction | profile/hardware/client launch/evidence | HANDOFF_ACCEPTED not running proof | Runtime Host |
| Game Session Coordinator | managed session lifecycle | GameSession state | start/exit/session events | one foreground managed session v1 | Runtime Host |
| Shared App Coordinator | Shared App Game presentation | shared app policy/runtime projection | presentation/app lifecycle | no blanket window/process authority | Runtime Host |
| Compatibility Evaluator | support decisions | CompatibilityDecision | compatibility query | version/provenance scoped | Runtime Host / release knowledge |
| Mutation Coordinator | serialize conflicting major mutations | coordination state/lease semantics | mode/update/recovery mutation gate | protects coherence, not ownership | Runtime Host + Broker cooperation |
| Update Orchestrator | update transaction semantics | UpdateTransactionRecord | metadata/stage/apply/verify | signed artifact + privileged apply | Runtime Host |
| Recovery Coordinator | safe target selection/recovery result | RecoveryContext | recovery plan/apply/verify | safety priority enforced | Runtime Host |
| Observability Coordinator | diagnostic correlation | DiagnosticRecord | emit/query diagnostic evidence | diagnostics not truth | Runtime Host |

---

## 3. Adapter components

| Adapter | External authority/mechanism | SplitOS output | Status/constraint |
|---|---|---|---|
| Windows Session Adapter | WTS / Win32 | current Windows user/session evidence | VERIFIED family |
| Process/Application Adapter | Win32 process/window APIs | process/app evidence | evidence limited to what API proves |
| Display Adapter | CCD APIs | display capability/actual state + apply result | read-back required |
| Audio Adapter | Core Audio/MMDevice | endpoint/default/event evidence | default endpoint switching OPEN |
| Input Adapter | Windows/device APIs | input/controller evidence | exact mapping/details later |
| Power Adapter | PowrProf | power scheme state/apply result | VERIFIED family |
| Service/System Adapter | SCM/system mechanisms | bounded machine operation result | privilege may require Broker |
| Steam Adapter | Steam-specific mechanisms | client/library/launch evidence | capability-specific; metadata may be version-sensitive |
| Epic Adapter | Epic-specific mechanisms | same semantic contract | exact capabilities OPEN/CANDIDATE |
| Xbox Adapter | Xbox/package mechanisms | same semantic contract | exact capabilities OPEN/CANDIDATE |
| Battle.net Adapter | Battle.net mechanisms | same semantic contract | exact capabilities OPEN/CANDIDATE |

---

## 4. Privileged components

| Component | Capability | Explicitly not responsible for |
|---|---|---|
| Privileged Broker | bounded privileged machine execution | mode truth, entitlement truth, UX, arbitrary admin shell |
| Machine Policy Handler | allowlisted policy changes | arbitrary registry writes |
| Service Policy Handler | allowlisted service lifecycle policy | generic control of any service |
| Update Apply Handler | verified/staged update actions | declaring update semantically successful |
| Recovery Apply Handler | authorized recovery mutations | choosing recovery target independently |

Trust path:

```text
Runtime Host
→ caller/session/capability validation
→ Broker
→ bounded handler
→ technical result
→ Runtime semantic verification
```

---

## 5. Persistence components

| Repository | Contents | Authority model |
|---|---|---|
| Local Canonical State Repository | committed mode/runtime durable facts as required | semantic owners write through repository |
| User Configuration Repository | Game Profiles/user/mode preferences | user/product owners |
| Transaction Journal | transition/update/recovery durable state | orchestrator-owned |
| External Projection Cache | client/library/device/account evidence | source/freshness-tagged cache only |
| Protected Secret Store | reusable account credentials/tokens | protected user-scoped secret material |
| Diagnostic Store | correlated logs/events | evidence only |

---

## 6. Backend components

| Component | Canonical responsibility | Client trust rule |
|---|---|---|
| Account Service | SplitOS Account | desktop cannot self-assert identity |
| Auth/Token Service | native client auth/token lifecycle | external browser + PKCE candidate |
| Entitlement Service | FREE/PRO capability entitlement | local editable flag forbidden |
| Subscription Reconciliation | payment evidence → entitlement decision input | provider evidence validated server-side |
| Offline Entitlement Issuer | bounded offline premium proof if adopted | signature/expiry/context binding required |
| Release Metadata Service | approved metadata delivery surface if adopted | metadata trust comes from signatures/provenance, not transport |

---

## 7. Build/release components

| Component | Responsibility | Required verification |
|---|---|---|
| Media Builder | prepare baseline | post-build baseline verification |
| Windows Source Validator | supported source acceptance | provenance/version/edition checks |
| Build Manifest Executor | typed transformations | manifest signature/version/action validation |
| Baseline Verifier | verify component/system result | required postconditions |
| Release Signing Authority | authorize release metadata/artifacts | protected key lifecycle |
| Artifact Repository | distribute artifacts | digest/signature validation at consumer |
| Key Lifecycle Authority | rotation/revocation/emergency policy | exact hierarchy OPEN |

---

## 8. Major state ownership matrix

| Canonical fact | Owner | Storage/projection | Must never be independently written by |
|---|---|---|---|
| SplitOS Account | Backend Identity | backend + local reference | Manager/Runtime cache |
| Entitlement | Backend Entitlement | backend + bounded offline assertion | payment callback/local config |
| ManagedRuntimeAccessDecision | Product Identity & Entitlement | runtime decision | Game Launcher |
| OperationalModeState | Mode State owner | runtime/durable as policy requires | UI/Broker/adapters |
| ModeTransitionRecord | Transition Coordinator | transaction journal | Broker/UI |
| GameProfile | Game Profiles | user config | Optimization engine/adapters |
| GameInstallationProjection | Game Library Representation | projection cache | raw external parser alone |
| HardwareSnapshot | Hardware Evaluator | ephemeral/short cache | device API directly |
| CompatibilityDecision | Compatibility Management | versioned knowledge | update executor/client adapter |
| UpdateTransactionRecord | Update Orchestrator | durable journal | installer exit code |
| RecoveryContext/result | Recovery Coordinator | durable journal | Broker alone |
| InstalledBaselineIdentity | verified build/update lifecycle | machine canonical | installer/version file alone |

---

## 9. Major end-to-end component chains

### First Run / FREE-PRO

```text
Manager/First Run
→ Runtime Access Coordinator
→ Account/Auth Backend
→ Entitlement Service
→ ManagedRuntimeAccessDecision
→ FREE Desktop | PRO mode startup
```

### Work → Game

```text
Manager/Game request
→ Operational Mode Coordinator
→ Mode Transition Coordinator
→ Application Lifecycle + Mode Policy + System Context
→ Windows adapters / Broker
→ actual-state verification
→ commit GAME
→ Game Launcher
```

### Managed Game Launch

```text
Game Launcher
→ Game Launch Orchestrator
→ Library + Profile + Hardware + Optimization
→ System Context verification
→ Game Client Adapter
→ external client
→ game process evidence
→ Game Session Coordinator = GAME_RUNNING
```

### Update

```text
Manager/runtime trigger
→ Compatibility + Entitlement
→ Update Orchestrator
→ Release trust validation
→ Transaction Journal
→ Broker update handler
→ reboot/resume
→ verification
→ InstalledBaselineIdentity commit
```

### Recovery

```text
Failure evidence
→ Recovery Coordinator
→ recovery target
→ Broker recovery handler
→ actual-state verification
→ stable state commit or manual escalation
```

---

## 10. Architecture-level OPEN decisions

| Area | Open decision | Why not guessed here |
|---|---|---|
| Local storage | DB/files/registry/schema | physical model not yet specified |
| Runtime module packaging | assemblies/process split | logical ownership already sufficient |
| IPC wire protocol | serializer/versioning/SDDL | security/spec detail |
| Broker identity | service account/hardening | requires Windows security validation |
| Auth provider | OAuth/OIDC provider/redirect method | product/backend choice |
| Offline PRO | assertion format/TTL/device binding | abuse/offline UX trade-off |
| Audio default switching | supported mechanism | public supported mechanism not validated |
| Game clients | Epic/Xbox/Battle.net exact APIs | evidence insufficient |
| Update engine | package/snapshot/rollback tech | compatibility/recovery engineering work |
| Windows source | supported acquisition/provenance | Microsoft/legal/technical validation |
| Component Matrix | exact REMOVE/DISABLE/MODE_MANAGED/KEEP | requires test matrix |
| Mutation coordination | lock/lease/transaction mechanism | semantic rule established, implementation open |

---

## 11. Result

This matrix is the condensed architecture handoff:

```text
Requirement/Decision
→ Responsibility
→ Owner
→ Component
→ Contract
→ Deployment/Trust boundary
→ Data placement
→ Flow/Failure behavior
```

Detailed Specification should refine these components without changing their canonical ownership semantics unless new verified evidence explicitly supersedes the current baseline.