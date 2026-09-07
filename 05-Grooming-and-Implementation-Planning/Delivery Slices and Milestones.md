# Delivery Slices and Milestones

## 1. Purpose

Defines an incremental implementation order that produces useful evidence early while preserving the final SplitOS architecture.

The goal is not to implement SPEC-01..14 in numeric order.

The goal is to repeatedly produce a coherent vertical slice:

```text
intent
→ contract
→ implementation
→ actual evidence
→ verification
```

---

## 2. Slice rules

Every slice must define:

- purpose;
- user/system-visible result;
- included components;
- explicit exclusions;
- dependencies;
- required engineering decisions;
- acceptance evidence;
- rollback/recovery expectation where mutation exists.

A slice may implement only a subset of final capabilities, but it must not introduce a temporary architecture that contradicts the final contracts unless the deviation is explicitly disposable and isolated.

---

# SLICE-00 — Engineering Foundation

## Goal

Create a buildable development skeleton for the real SplitOS process boundaries.

## Scope

```text
RuntimeHost process
Broker Windows Service
Manager process shell
GameLauncher process shell
shared contract/schema project
basic structured diagnostics
basic build/version metadata
```

Required proof:

```text
Windows logon / manual dev start
↓
Runtime Host starts unelevated
↓
Broker service available
↓
Runtime ↔ Broker secure hello over Named Pipe
↓
Manager ↔ Runtime hello
↓
GameLauncher ↔ Runtime hello
↓
component versions reported
```

No machine mutation is required yet beyond an explicitly harmless/read-only Broker capability such as health/version.

## Blocking decisions

- implementation language/runtime;
- UI framework baseline;
- solution/build system;
- schema/code-generation approach if any;
- unit/integration test framework baseline.

## Acceptance

- four process types build independently;
- Broker runs under specified service identity candidate;
- UI cannot directly call Broker;
- per-session pipe naming works;
- caller/session evidence is observable;
- incompatible protocol version fails clearly;
- structured correlation/event envelope works;
- CI/local build can produce identified artifacts.

## Milestone

`M0 — Engineering Skeleton`

---

# SLICE-01 — Local State and FREE Runtime Skeleton

## Goal

Prove canonical persistence and a stable FREE machine experience before premium mode mutation.

## Scope

```text
machine.db
user.db
projection.db
migrations baseline
Runtime Host startup/reconciliation
Manager status view
ManagedRuntimeAccess = DISABLED
OperationalMode = NONE
```

Account backend can initially be replaced by a deterministic development entitlement fixture if real auth is not ready, but the fixture MUST remain behind the same semantic entitlement interface and cannot be shipped as production authority.

## Dependencies

`SLICE-00`

## Acceptance

- canonical stores created with correct writer boundaries;
- Runtime Host restart preserves durable user/machine state;
- projection cache can be deleted/rebuilt without authority loss;
- ordinary UI process cannot write machine store directly;
- FREE state never enables privileged managed mode actions;
- Windows desktop remains usable when Runtime/Manager fail.

## Milestone contribution

Foundation for `M2 — FREE Product Vertical`.

---

# SLICE-02 — Account and Entitlement Vertical

## Goal

Replace development entitlement fixtures with the real SplitOS Account / FREE / PRO path.

## Scope

```text
Manager First Run
external browser auth
loopback callback
PKCE
account association
protected token storage
entitlement refresh
FREE/PRO projection
offline assertion validation baseline
```

Checkout/payment can be introduced after base account/entitlement if it shares the same entitlement refresh contract.

## Dependencies

- `SLICE-00`;
- `SLICE-01`;
- backend auth/entitlement implementation;
- protected-secret implementation decision.

## Acceptance

- Windows account and SplitOS account remain distinct;
- login failure/backend outage does not block Windows desktop;
- callback cannot directly grant PRO;
- FREE remains stable;
- valid PRO capability appears only after backend/offline proof validation;
- secrets are not ordinary SQLite plaintext fields;
- another Windows user cannot automatically reuse first user's account token.

## Milestone

`M2 — FREE Product Vertical`

---

# SLICE-03 — Mode Transaction Foundation

## Goal

Prove the durable mode state machine without broad Windows policy coverage.

## Scope

```text
ACTIVATE / SWITCH / DEACTIVATE
ModeTransition journal
machine mutation lease
fencing token
blocker result model
BASE / WORK / GAME policy skeleton
commit / cancel / rollback semantics
Runtime Host crash reconciliation
```

Use minimal real machine mutations at first, for example:

```text
power policy
one validated managed service
```

Display can join in SLICE-04 once target resolution is proven.

## Dependencies

- `SLICE-01` persistence;
- `SLICE-02` for real PRO gating, although dev fixtures can unblock early engineering;
- Broker typed capability path.

## Acceptance

- target mode never becomes canonical before verification;
- crash before commit preserves source canonical mode;
- crash after commit preserves target canonical mode;
- stale fencing token is rejected;
- user cancellation does not become failure;
- rollback success does not invent a canonical `ROLLED_BACK` mode/state;
- only physical console session can control machine-wide mode.

---

# SLICE-04 — Windows Context and Real WORK ↔ GAME

## Goal

Turn mode transaction semantics into a visibly different and verified machine context.

## First supported policy subset

```text
Display
Power
Managed Service subset
Process/blocker evidence
Input/controller availability
Hardware generation invalidation
```

Audio observe/read is included. Automatic default switching remains optional until supported.

## Dependencies

- `SLICE-03`;
- Windows adapter prototypes;
- display identity/selector fixtures.

## Acceptance

- WORK→GAME applies desired supported context;
- actual Windows state is read back;
- stale display/hardware snapshot blocks/re-resolves apply;
- display unplug during transition converges to rollback/fallback;
- Work blockers requiring user action pause/cancel correctly;
- transition commit is based on mandatory predicates;
- GAME→WORK restores verified Work target.

## Milestone

`M3 — Managed Mode Vertical`

---

# SLICE-05 — Game Launcher + Steam MVP

## Goal

Deliver the first genuine gaming vertical.

## Scope

```text
Game Launcher lifecycle
controller-first focus/navigation
Steam client detection
Steam library/install projection subset
steam:// launch handoff
Game Session lifecycle
process correlation
exit confirmation
return to Launcher
```

Initial game support should be intentionally small and fixture-driven.

## Dependencies

- `SLICE-04` GAME mode;
- GameInput integration;
- Steam adapter prototype;
- process evidence adapter.

## Acceptance

```text
WORK or GAME
→ GAME committed
→ Launcher ACTIVE
→ user selects verified Steam game
→ HANDOFF_ACCEPTED
→ actual game process evidence
→ GAME_RUNNING
→ game exits
→ Launcher restored
→ committed mode still GAME
```

Negative acceptance:

- Steam not installed;
- auth/login required;
- handoff accepted but game never starts;
- bootstrap process replaced by game process;
- game crash;
- Runtime Host restarts while game is running.

## Milestone

`M4 — Gaming MVP`

---

# SLICE-06 — Game Profiles, TV Scenario and Optimization Baseline

## Goal

Make Game Mode scenario-aware instead of one global configuration.

## Scope

```text
multiple Game Profiles
Desktop scenario
TV/living-room scenario
display/input selectors
field-level user locks
ResolvedProfileContext
small per-game config adapter set
explainable recommendation engine
external drift handling
```

Optional measured performance telemetry can be introduced after deterministic static recommendation works.

## Dependencies

- `SLICE-05` gaming vertical;
- stable hardware/display generation model;
- at least one game config adapter.

## Acceptance

- Desktop and TV profiles remain independent;
- disconnected TV does not rewrite profile intent;
- ambiguous hardware match requires deterministic handling/user choice;
- user-locked field is never silently changed by optimizer;
- unsupported lock is suspended rather than deleted;
- game config write uses source conflict detection and read-back;
- external game config changes are preserved for immediate run and surfaced for reconciliation.

## Milestone

`M5 — Profile/TV Alpha`

---

# SLICE-07 — Shared Apps and Game Experience Expansion

## Goal

Add managed companion app presentation without compromising game/client boundaries.

## Scope order

```text
BACKGROUND
SECONDARY_DISPLAY
LOCKED_WINDOW
OVERLAY where proven
optional in-game panel where proven
```

## Dependencies

- Launcher/game session stability;
- window evidence/orchestration adapter;
- hardware display topology.

## Acceptance

- maximum three active assignments;
- HWND recreation does not become stale permanent identity;
- repeated placement failure converges to degraded state;
- app cannot steal foreground through SplitOS without explicit user action;
- no injection/hooking required;
- unsupported overlay context reports unavailable rather than forcing it.

---

# SLICE-08 — Builder and Prepared Baseline

## Goal

Move from “SplitOS components running on developer Windows” to an actual prepared SplitOS installation baseline.

This track can begin in parallel with SLICE-01..05 after technology foundation is stable.

## Scope

```text
source identity
Build Manifest schema
Component Matrix minimal accepted subset
offline servicing executor
SplitOS package staging
setup provisioning
BuildReceipt
clean-install lab flow
```

Start with conservative component policy:

```text
KEEP core dependencies
REMOVE only strongly validated removable consumer AppX subset
MODE_MANAGED only validated service/feature subset
leave risky Defender/Edge hypotheses out of production manifest
```

## Acceptance

- supported source is accepted and unsupported source rejected;
- typed manifest only;
- mandatory operation/postcondition failure fails build;
- clean install boots;
- Windows OOBE works;
- first Windows user can sign in;
- Runtime/Broker provisioning appears correctly;
- resulting baseline identity matches BuildReceipt.

## Milestone

`M1 — Prepared Baseline Boots`

Note: milestone numbering reflects evidence importance, not strict serial implementation order. Builder can be developed in parallel.

---

# SLICE-09 — Update, Recovery and User-Data-Preserving Rollback

## Goal

Make installed SplitOS recoverable before broader release expansion.

## Scope

```text
SplitOS update client
release staging
UpdateTransaction
Update Bootstrap
previous-release Recovery Capsule
reboot/resume
health verification
software rollback
WinRE Recovery Tool baseline
user-data rollback compatibility
```

## Dependencies

- prepared baseline;
- persistence/migrations;
- Runtime/Broker packaging;
- release artifact identity;
- recovery storage prototype.

## Acceptance

- update cannot activate without verified previous-release capsule;
- interruption before commit preserves source release canonical identity;
- interruption after commit treats target as canonical then repairs/recovers;
- previous-release restore requires exact recovery authorization once security layer is integrated;
- rollback preserves current user profile/preferences;
- recovery does not become arbitrary shell;
- Windows-level corruption is handed to Windows-native recovery path.

## Milestone contribution

`M6 — Resilient Alpha`

---

# SLICE-10 — Release Trust and Secure Distribution

## Goal

Replace development release trust with production-shaped repository and executable trust.

## Scope

```text
TUF client
embedded test/prod root handling
release/knowledge/recovery delegated metadata
anti-rollback floors
Authenticode verification
publisher policy
key-rotation/revocation fixtures
RecoveryAuthorization
```

Production HSM/CA procurement can be a separate operational workstream; code must first work with equivalent test keys and role separation.

## Dependencies

- update/recovery artifact formats;
- packaging/release pipeline;
- security metadata schemas.

## Acceptance

- CDN bytes alone cannot authorize release;
- TUF-only artifact without allowed publisher is rejected;
- publisher-signed artifact not authorized by release metadata is rejected;
- old authentic release normal downgrade is rejected;
- exact authorized recovery edge succeeds;
- metadata/publisher key rotation/revocation fixture tests pass.

---

# SLICE-11 — Observability, Verification Automation and Release Candidate Path

## Goal

Turn working alpha behavior into repeatable support/acceptance evidence.

Observability starts earlier in minimal form; this slice completes the product-level system.

## Scope

```text
structured local event catalog
security audit
WER LocalDumps setup
ETW diagnostic capture
support bundle/redaction
SPEC-14 gate runner/evidence model
compatibility matrix fixtures
fault-injection harness
performance benchmark harness
ReleaseAcceptanceProfile
ReleaseReadinessRecord
```

## Acceptance

- diagnostic records cannot become canonical state;
- synthetic secret export tests fail closed;
- no implicit cloud telemetry;
- major mode/update/recovery faults have reproducible tests;
- supported matrix is frozen for candidate;
- numeric performance thresholds are bound before production gate;
- exact candidate artifacts are bound to readiness evidence.

## Milestone

`M7 — Release Candidate Infrastructure`

---

## 3. Parallel execution view

```text
Runtime track:
S00 → S01 → S03 → S04 → S05 → S06 → S07
          ↑
          S02 Account integrates before production PRO

Distribution track:
S00 → S08 → S09 → S10

Verification track:
starts S00
→ grows every slice
→ completed S11
```

---

## 4. First prototype recommendations

### Prototype A — Secure local skeleton

Deliver through S00.

No UI polish.

### Prototype B — FREE desktop vertical

Deliver S01 + S02.

### Prototype C — Real mode switching

Deliver S03 + S04 on a small supported hardware set.

### Prototype D — Gaming MVP

Deliver S05 with one Steam game first, then a small matrix.

### Prototype E — Prepared SplitOS image

Merge S08 outputs with Prototype C/D runtime.

### Prototype F — Resilient alpha

Add S09/S10 and destructive fault tests.

---

## 5. Definition of slice completion

A slice is complete when:

```text
scope implemented
+ relevant contracts versioned
+ migrations handled
+ expected behavior demonstrated
+ negative/edge cases demonstrated
+ diagnostics sufficient
+ required verification evidence stored
+ newly discovered system gaps returned to SSAD knowledge
```

A demo alone is not completion.