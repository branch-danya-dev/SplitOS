# QA and Acceptance Handoff

## 1. Purpose

Defines how implementation slices hand evidence to QA and how incremental verification stays aligned with SPEC-14 without waiting until the final release candidate.

The principle is:

```text
implementation task
!= complete because code compiles

implementation task
→ behavior evidence
→ negative evidence
→ diagnostics
→ traceability
```

---

## 2. Verification layers

### L0 — Unit / pure component

Purpose:

- state transitions;
- policy resolution;
- schema validation;
- deterministic selection;
- error classification;
- redaction;
- cryptographic metadata logic with fixtures.

No real Windows machine mutation required.

### L1 — Component integration

Purpose:

- persistence + owner;
- IPC serialization/versioning;
- adapter contract + fixture;
- backend contract + mock/test service;
- release metadata + artifact fixture.

### L2 — Windows integration

Purpose:

- service install/start;
- Named Pipe caller/session checks;
- SQLite filesystem/ACL behavior;
- display/power/service APIs;
- reboot/resume;
- WinRE/bootstrap integration where practical.

Runs in disposable supported Windows environments.

### L3 — Physical hardware / gaming lab

Purpose:

- real GPU/display/controller;
- high-refresh / TV topology;
- hot-plug;
- GameInput devices;
- Steam/Epic/Microsoft Gaming;
- real game process correlation;
- frame-time/performance evidence;
- overlay/window behavior.

### L4 — Fault / security / recovery

Purpose:

- crash checkpoints;
- disk-full/write failure;
- stale fencing;
- malicious IPC;
- tampered release artifacts;
- update interruption;
- Recovery Capsule corruption;
- rollback/data preservation;
- redaction failure.

### L5 — Release acceptance

Purpose:

- frozen ReleaseAcceptanceProfile;
- supported compatibility cells;
- production-shaped trust;
- numeric performance thresholds;
- GATE-00..12;
- ReleaseReadinessRecord.

---

## 3. Slice → acceptance mapping

| Delivery slice | Minimum verification families |
|---|---|
| SLICE-00 Foundation | L0 contracts, L1 IPC, L2 service/session/caller checks, negative Broker surface |
| SLICE-01 Persistence/FREE skeleton | L0 repository semantics, L1 migration fixtures, L2 ACL/durability/restart |
| SLICE-02 Account/Entitlement | auth contract tests, FREE/PRO negative tests, backend outage, token isolation, callback forgery |
| SLICE-03 Mode transaction | state-machine tests, crash checkpoint tests, fencing/stale-owner tests |
| SLICE-04 Windows context | Windows read/apply/read-back, device generation, hot-unplug, blocker scenarios |
| SLICE-05 Gaming MVP | client handoff, game start proof, timeout/auth, process replacement, exit, Runtime restart |
| SLICE-06 Profiles/Optimization | selector determinism, user lock precedence, config conflict/read-back, drift fixtures |
| SLICE-07 Shared Apps | HWND generation, placement read-back, focus, unplug, retry/degraded, no injection |
| SLICE-08 Builder | source rejection, manifest validation, servicing postconditions, clean install/OOBE/runtime startup |
| SLICE-09 Update/Recovery | update phase interruption, reboot resume, capsule requirement, rollback data preservation |
| SLICE-10 Release Trust | TUF negative suite, Authenticode, anti-rollback, recovery authorization, key rotation fixtures |
| SLICE-11 Observability/RC | event authority tests, audit, bundle redaction, retention, performance and full gate evidence |

---

## 4. Acceptance criteria style

Use observable system semantics.

Good:

```text
Given committed WORK and a valid PRO entitlement
When user requests GAME and the target display disappears before apply
Then GAME is not committed
And the stale resolved display target is not used
And the transition converges to verified WORK or specified safe fallback
```

Weak:

```text
Switch mode handles errors correctly
```

Good:

```text
Given Steam accepted the protocol launch request
When no correlated game process reaches a strong/accepted proof set before timeout
Then GameSession does not become GAME_RUNNING
And Launcher returns to an actionable failure state
And committed mode remains GAME
```

---

## 5. Every mutating flow needs three classes of tests

### Happy path

Target is reached and verified.

### Controlled non-success

Examples:

- user cancels;
- auth required;
- unsupported capability;
- blocker needs user decision;
- target device unavailable.

These are not all system failures.

### Failure/convergence

Examples:

- operation partially applied;
- Runtime/Broker crashed;
- read-back mismatched;
- durable write failed;
- rollback failed;
- recovery required.

The test target is safe convergence, not merely an error code.

---

## 6. Verification evidence contract per implementation issue

When an issue is completed, attach or link:

```text
SPEC references
implementation commit/PR
unit/component test IDs
integration case IDs
observed result
relevant diagnostic correlation/event examples
known unsupported matrix cells
new OPEN/evidence discovered
```

For risky Windows integration also capture:

```text
Windows build
hardware/driver version
client/game version where applicable
```

---

## 7. Fixtures as first-class delivery artifacts

Create reusable fixtures for:

### Persistence

- empty DB;
- previous supported schema;
- partially migrated schema;
- corrupt projection cache;
- corrupt canonical DB fixture where recovery behavior is testable;
- disk-full/write-failure simulation.

### Entitlement

- FREE;
- PRO;
- expired assertion;
- wrong account/installation binding;
- clock rollback;
- backend unavailable;
- checkout callback without entitlement.

### Mode

- no blockers;
- auto-resolvable blocker;
- user-decision blocker;
- hard blocker;
- stale mutation lease;
- crash before commit;
- crash after commit.

### Game Client

- client absent;
- client login required;
- game installed;
- installation stale/unknown;
- handoff rejected;
- handoff accepted/no game;
- bootstrap→game process replacement;
- ambiguous candidate process;
- game crash/normal exit.

### Release security

- valid metadata chain;
- expired timestamp;
- stale snapshot;
- mix-and-match targets;
- wrong delegated role;
- valid TUF/wrong publisher;
- valid publisher/unapproved target;
- revoked key;
- unauthorized downgrade;
- valid recovery edge.

### Diagnostics

- synthetic OAuth/token/cookie/private-key-like values;
- user paths;
- stable device IDs;
- oversized logs;
- redaction failure.

---

## 8. CI / lab lanes

Exact CI vendor remains an implementation decision, but logical lanes should be stable.

### PR_FAST

Runs on most changes:

```text
format/static checks
unit tests
schema validation
contract fixtures
pure state/security tests
```

### PR_COMPONENT

Runs where affected:

```text
IPC component tests
SQLite migration/backup tests
backend contract tests
TUF fixture tests
```

### WINDOWS_INTEGRATION

Runs on disposable Windows:

```text
service/pipe/session
ACL/persistence
power/services/process adapters
install/package
reboot/update fixtures
```

### HARDWARE_LAB

Scheduled/required for affected supported scope:

```text
display
controller
GPU/game
client adapters
performance
Shared Apps
```

### DESTRUCTIVE_FAULT

Runs on isolated disposable systems:

```text
process kills
power-loss-equivalent update interruption
corrupt capsule
recovery interruption
disk pressure
```

### RELEASE_CANDIDATE

Runs frozen acceptance profile against exact candidate hashes.

---

## 9. QA must be able to answer “what changed?”

Every groomed task should identify change surface:

```text
Owners affected
Contracts affected
State transitions affected
Persistence/migration affected
Windows/client integration affected
Security boundary affected
Acceptance cases affected
```

If this cannot be stated, the task is not sufficiently groomed.

---

## 10. Regression ownership

When a contract/owner changes, regression is not limited to the changed project.

Examples:

### Broker protocol change

Regression includes:

- Runtime Broker client;
- caller/auth checks;
- idempotency;
- stale fence behavior;
- update/recovery privileged calls;
- diagnostics/audit fields.

### GameProfile schema change

Regression includes:

- user-store migration;
- selector;
- optimizer;
- Launcher profile UX;
- game config apply;
- software rollback one-version compatibility.

### Release metadata change

Regression includes:

- updater;
- recovery capsule;
- TUF client;
- anti-rollback;
- release signing pipeline fixtures;
- BuildReceipt/ReleaseReadiness evidence.

---

## 11. Handling unsupported capability during development

If implementation cannot satisfy a capability:

```text
case FAIL
↓
Is capability mandatory for current slice/support profile?
├── YES → slice/release blocked
└── NO  → remove/downgrade support claim
         and keep explicit unsupported/experimental state
```

Do not change expected result to make the test green without updating scope/knowledge.

---

## 12. Definition of QA-ready implementation task

A task is ready for QA when:

- target behavior is observable;
- acceptance cases exist;
- required fixtures/environment exist;
- diagnostics identify the operation;
- relevant contracts are versioned;
- migration path exists if state changed;
- supported/unsupported scope is explicit;
- no critical OPEN is hidden in code comments.

---

## 13. Production handoff

Incremental test passes are not automatically production evidence.

Before release:

```text
freeze ReleaseCandidate
↓
freeze ReleaseAcceptanceProfile
↓
run required matrix cells/gates
↓
collect immutable result references
↓
ReleaseReadinessRecord
↓
required sign-offs
```

This preserves SPEC-14's rule that production readiness applies to one exact candidate, not to a moving branch.