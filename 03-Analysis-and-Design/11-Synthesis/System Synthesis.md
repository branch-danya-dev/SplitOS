# SplitOS — System Synthesis

## 1. Purpose

Этот документ собирает предыдущие A&D layers в единую implementable architecture view.

Synthesis не создаёт новых competing owners/states. Он связывает уже канонические:

```text
Boundaries
→ Responsibilities
→ Ownership
→ States
→ Behavior
→ Data
→ Interfaces
→ Integrations
→ Flows
→ Failures
→ Trust
```

в конечную системную композицию.

---

## 2. Product architecture in one view

SplitOS состоит из двух разных продуктовых частей:

```text
Build-time product
+
Installed runtime product
```

### Build-time

```text
Microsoft-authorized Windows source
        +
SplitOS Media Builder
        +
Signed/versioned Build Manifest
        +
SplitOS packages
        +
Windows Component Matrix
        ↓
Source validation
        ↓
Typed transformations / supported servicing
        ↓
Baseline verification
        ↓
Prepared SplitOS installation media/baseline
```

### Installed system

```text
Windows 11 baseline prepared by SplitOS
        ↓
Windows OOBE
        ↓
Windows User
        ↓
SplitOS Account association
        ↓
Entitlement
   ┌────┴────┐
   │         │
 FREE       PRO
   │         │
normal      SplitOS Managed Runtime
Windows     ↓
Desktop     WORK xor GAME
```

Critical product rule:

```text
Installed SplitOS
!= Paid entitlement
```

The baseline exists independently from premium runtime activation.

---

## 3. Final logical component set

### 3.1 SplitOS Media Builder

Owns build-time preparation of a supported baseline.

Responsibilities:

- validate Windows source;
- load/version-check Build Manifest;
- apply typed baseline transformations;
- install SplitOS runtime packages;
- verify expected component state;
- produce supported baseline identity/evidence.

It does not own runtime Work/Game decisions.

---

### 3.2 SplitOS Manager

Interactive desktop control center.

Responsibilities:

- First Run / account onboarding surface;
- account/subscription status;
- upgrade/manage subscription entry point;
- Work/Game configuration;
- Game Profiles management;
- device/profile/settings management;
- update/recovery user surfaces;
- diagnostic/support surface where allowed.

Manager is a consumer of semantic runtime contracts; it is not canonical owner of mode/entitlement/update state.

---

### 3.3 SplitOS Game Launcher

Primary Game Mode UX.

Responsibilities:

- controller-first game browsing/navigation;
- unified Game Library projection presentation;
- launch requests;
- profile selection/presentation;
- Shared App Game UX;
- game-session status presentation;
- explicit Switch to Work entry point.

Game Launcher does not own game license/install truth, operational mode truth or privileged Windows mutation.

---

### 3.4 SplitOS Runtime Host

Primary interactive-session orchestration process.

This is the semantic composition root of the installed runtime.

Responsibilities include orchestration of:

- current Windows user context;
- SplitOS account association/access resolution;
- mode intent and mode-transition workflows;
- application lifecycle policy coordination;
- display/audio/input/power contexts;
- hardware evidence refresh;
- game library/profile/optimization resolution;
- Game Client adapters;
- game launch/session orchestration;
- update/recovery coordination at semantic level;
- diagnostics/event correlation;
- authenticated requests to Privileged Broker.

Important:

```text
Runtime Host
!= one giant ownership object
```

Internally it should preserve the responsibility boundaries already defined in `01-Responsibilities` and `02-Ownership`.

---

### 3.5 SplitOS Privileged Broker

Narrow machine-mutation Windows Service / privileged process.

Responsibilities:

- execute allowlisted privileged capabilities requested by authorized Runtime Host context;
- protected service/policy/system mutations;
- privileged update/recovery operations;
- return technical result/evidence for semantic verification.

Explicitly not allowed as product contract:

```text
RunCommand(anything)
RunPowerShell(anything)
WriteRegistry(anything)
ControlAnyService(anything)
```

Broker does not decide canonical mode, entitlement or update success.

---

### 3.6 Windows Context Adapters

Logical adapters around supported Windows APIs/evidence sources.

Families:

```text
Session Context Adapter
Process/Application Evidence Adapter
Display Context Adapter
Audio Context Adapter
Input Context Adapter
Power Context Adapter
Service/System Adapter
```

Their role is translation:

```text
SplitOS semantic request
↔ Windows mechanism/evidence
```

They do not become owners of SplitOS product truth.

---

### 3.7 Game Client Adapter Layer

One stable SplitOS semantic contract with per-client implementations.

```text
Game Client Contract
├── Steam Adapter
├── Epic Adapter
├── Xbox Adapter
└── Battle.net Adapter
```

Adapter capabilities may differ per client and version.

Responsibilities:

- client availability evidence;
- library/install projection evidence;
- launch handoff;
- process/start/exit correlation where supported;
- normalize external state into bounded SplitOS evidence.

External client remains authoritative for its own auth/license/install/store truth.

---

### 3.8 Local State Store

Logical persistence boundary for SplitOS-owned local durable state.

Contains only data whose lifecycle requires local persistence, such as:

- Windows user ↔ SplitOS account association metadata;
- Game Profiles/preferences;
- committed operational state if durability policy requires it;
- durable transition/update/recovery records;
- installed baseline identity;
- compatible cached/offline entitlement evidence;
- selected configuration/policy material;
- diagnostics according to retention policy.

It must not convert external projections into authoritative external truth.

Exact physical storage engine remains Specification-level.

---

### 3.9 SplitOS Account Backend

Server-side product identity/entitlement authority.

Responsibilities:

- SplitOS Account authentication/integration;
- session/token issuance/validation;
- canonical entitlement;
- subscription/payment reconciliation;
- offline entitlement assertion issuance if adopted;
- device/account capability policy if adopted;
- release/update metadata delivery where architecture later assigns it.

The backend is not part of Windows authentication.

Backend outage must not intentionally block Windows sign-in/base Desktop.

---

### 3.10 Payment Provider

External authority for payment transaction evidence.

It does not own SplitOS entitlement directly.

Canonical chain:

```text
Payment Provider evidence
→ SplitOS Backend validation
→ Entitlement
```

---

### 3.11 Release / Build Authority

Logical release trust domain.

Responsibilities:

- authorize release manifests/artifacts;
- protect signing authority;
- key rotation/revocation policy;
- define allowed upgrade/downgrade/recovery transitions;
- bind release identity to exact component set/digests.

---

## 4. Runtime composition

Canonical installed topology:

```text
┌──────────────── Windows interactive user session ────────────────┐
│                                                                  │
│  SplitOS Manager                SplitOS Game Launcher            │
│          │                              │                        │
│          └──────── semantic requests ───┘                        │
│                         ↓                                        │
│                 SplitOS Runtime Host                             │
│                  │      │       │                                │
│                  │      │       ├── Game Client Adapters         │
│                  │      ├────────── Windows Context Adapters     │
│                  │                                               │
│                  ├── Local State Store                           │
│                  │                                               │
│                  ├── HTTPS → SplitOS Account Backend             │
│                  │                                               │
│                  └── authenticated/authorized local IPC          │
└───────────────────────────────┼───────────────────────────────────┘
                                ↓
                    SplitOS Privileged Broker
                    Windows Service / Session 0
                                ↓
                     privileged Windows operations
```

External side:

```text
Runtime Host
├── Steam / Epic / Xbox / Battle.net
├── SplitOS Account Backend
└── Windows / Driver / Device evidence

Account Backend
└── Payment Provider
```

---

## 5. Runtime access synthesis

### FREE

```text
Windows signed in
→ SplitOS Account resolved
→ effective entitlement = FREE
→ ManagedRuntime = DISABLED
→ OperationalMode = NONE
→ normal Windows Desktop
```

Allowed SplitOS surfaces may still include Manager/account/update/base settings according to product policy.

Normal Windows applications and games may run through normal Windows/client paths.

### PRO

```text
Windows signed in
→ entitlement proves managed capability
→ ManagedRuntime = ENABLED
→ mode selection/startup policy
→ WORK xor GAME
```

Only PRO managed runtime enters canonical Work/Game orchestration.

---

## 6. Operational mode synthesis

Canonical separation:

```text
User mode intent
!= Committed Operational Mode
!= Mode Transition lifecycle
!= Game Session lifecycle
```

Example Work→Game:

```text
CommittedMode = WORK
TransitionTarget = GAME
TransitionState = APPLYING
```

remains valid until verified commit.

Only then:

```text
CommittedMode = GAME
TransitionTarget = null
TransitionState = IDLE
```

No `SWITCHING` operational mode and no `HYBRID` operational mode are introduced.

---

## 7. Game launch synthesis

Canonical path:

```text
GameLaunchRequest
→ ensure ManagedRuntime access
→ ensure committed GAME
→ reconcile Game/GameClient projection
→ refresh hardware/display/input evidence
→ resolve GameProfile
→ resolve optimization/effective launch context
→ apply + verify required local context
→ Game Client Adapter handoff
→ observe actual game evidence
→ GAME_RUNNING
```

Critical distinction:

```text
HANDOFF_ACCEPTED
!= GAME_RUNNING
```

On normal game exit:

```text
GAME_RUNNING
→ exit evidence
→ cleanup
→ Game Launcher

CommittedMode stays GAME
```

Direct game launch from Work is composition:

```text
FL-02 Work→Game
→ after GAME commit
→ FL-03 Managed Game Launch
```

---

## 8. Major mutation coordination

Three major orchestration families may require machine-level mutations:

```text
Mode Transition
Update
Recovery
```

They must coordinate exclusive/conflicting mutation windows.

A v1 implementation should not allow independent writers to interleave protected machine mutations without an explicit coordinator/lease/transaction policy.

The exact concurrency primitive remains Specification-level, but semantic exclusivity is canonical.

---

## 9. Failure and safe-convergence synthesis

Any mutating operation follows:

```text
intent
→ apply
→ actual-state evidence
→ verify
→ commit
```

Failure follows:

```text
failure evidence
→ owning responsibility
→ classify
→ Retry / Fallback / Cancel / Rollback / Recovery
→ apply response
→ read actual state
→ verify
→ commit only proven result
```

Safety priority:

```text
User data integrity
→ Windows bootability/base usability
→ known coherent state
→ correct SplitOS canonical state
→ managed runtime restoration
→ UX convenience
```

Therefore an unrecoverable PRO runtime failure may legitimately converge to usable base Windows instead of breaking the machine while trying to preserve premium behavior.

---

## 10. Trust synthesis

No component is globally trusted.

Sensitive action chain:

```text
request/claim
→ identity/provenance
→ integrity
→ freshness
→ context binding
→ capability authorization
→ semantic owner decision
→ operation
→ actual-state verification
```

Key boundaries:

```text
UI → Runtime Host
Runtime Host → Privileged Broker
Runtime Host → Account Backend
Runtime Host → Game Client Adapter → External Client
Updater/Builder → Release Trust Domain
```

Premium authorization must fail closed for premium capabilities while preserving base Windows usability.

External evidence cannot cross directly into privileged command execution.

---

## 11. Build/update synthesis

### Build

```text
Authorized Windows source
→ source validation
→ signed/versioned Build Manifest
→ typed transformations
→ SplitOS package installation
→ expected-state verification
→ prepared baseline identity
```

### Update

```text
update metadata
→ entitlement/compatibility decision
→ signed artifact/manifest validation
→ durable UpdateTransaction
→ protected staging
→ privileged apply
→ reboot/resume if required
→ actual-state verification
→ InstalledBaselineIdentity commit
```

A process exit code or successful installer return is not sufficient to commit the new baseline.

---

## 12. Canonical architecture principles

1. **Windows remains the platform.** SplitOS does not replace the kernel or Windows Shell globally.
2. **Build-time and runtime concerns stay separate.** Baseline construction does not own runtime mode behavior.
3. **FREE and PRO share one installed baseline.** Entitlement enables managed capabilities; it does not determine whether Windows may boot.
4. **Semantic owners remain authoritative.** UI, adapters, logs and external evidence do not create competing truth.
5. **Desired, actual and canonical state are distinct.** Technical success must be verified semantically.
6. **Privileged mutation is narrow.** User-session UI/orchestration remains separated from SYSTEM-level capability execution.
7. **External authorities remain external.** Steam/payment/Windows/device facts are consumed as bounded evidence.
8. **Failure converges safely.** Rollback/recovery are verified operations, not assumptions.
9. **Trust is capability-scoped.** Signed/connected/local does not mean globally authorized.
10. **Open mechanisms stay OPEN.** Synthesis does not fabricate APIs to close engineering gaps.

---

## 13. Remaining implementation decisions

Synthesis is implementable at architecture level but intentionally leaves detailed decisions for Specification/engineering validation.

Important OPEN items include:

- physical local storage engine/schema;
- exact internal module/package decomposition inside Runtime Host;
- exact local IPC protocol/serialization/SDDL/caller-token rules;
- Windows service account and service hardening details;
- exact OAuth/OIDC provider and redirect mechanism;
- offline entitlement assertion format, TTL and clock-rollback handling;
- exact release manifest envelope/key hierarchy/revocation mechanism;
- exact update package, snapshot and rollback technology;
- supported mechanism for system-wide default audio switching;
- exact Epic/Xbox/Battle.net integration capabilities;
- stable Steam local metadata strategy;
- Windows Component Matrix and validated REMOVE/DISABLE/MODE_MANAGED/KEEP decisions;
- Microsoft Windows source acquisition/provenance process;
- performance thresholds and observability retention;
- exact major-mutation concurrency primitive.

These are not architecture holes to fill by assumption; they are explicit Specification/engineering backlog.

---

## 14. Result

The final SplitOS architecture can be summarized as:

```text
Signed/validated build pipeline
        ↓
Prepared Windows baseline
        ↓
Windows user session
        ↓
SplitOS identity + entitlement
        ↓
FREE: normal Windows
or
PRO: Runtime Host orchestration
        ↓
WORK xor GAME
        ↓
verified Windows/Game Client operations
        ↓
Game Launcher / Work Desktop

Privileged mutations
→ narrow Broker

Canonical data/state
→ owner-controlled local/backend domains

External facts
→ bounded evidence adapters

Failures
→ verified safe convergence
```

This is the architecture baseline to carry into detailed Specification, implementation planning and verification design.