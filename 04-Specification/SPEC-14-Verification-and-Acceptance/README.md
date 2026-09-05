# SPEC-14 — Verification & Acceptance

## 1. Purpose

This package defines the final detailed-specification verification contract for SplitOS v1.

It does **not** introduce a new architecture layer. It translates the already-defined requirements and SPEC-01..13 contracts into executable release evidence.

The final chain is:

```text
Requirement / architectural invariant
→ owning SPEC contract
→ verification case
→ evidence
→ release gate
→ release verdict
```

The central rule is:

> A release is accepted because its required behavior was demonstrated on the declared support matrix, not because implementation work is “mostly complete”.

---

## 2. Scope

SPEC-14 covers verification of:

```text
Build / source / prepared baseline
Clean installation and Windows OOBE
FREE runtime behavior
SplitOS account and PRO entitlement
Runtime process / IPC / Broker boundaries
Mode activation / switching / deactivation
Windows context integrations
Game client adapters
Game launch / exit / process correlation
Game Profile / optimization behavior
Game Launcher / Shared Apps
Persistence / migration / corruption handling
Update / reboot / resume
Recovery Capsule / rollback / WinRE recovery
Release signing / TUF / Authenticode / anti-rollback
Observability / diagnostics / privacy
Performance / resource overhead
Hardware / Windows / client compatibility
Fault injection / crash / power-loss convergence
```

---

## 3. Non-goals

SPEC-14 does not:

- replace unit/integration test design inside implementation repositories;
- invent missing product requirements;
- silently choose values for requirements whose acceptance threshold is still `TBD`;
- declare unsupported hardware/client combinations supported merely because one test happened to pass;
- make diagnostic logs canonical state;
- make experimental capabilities release blockers unless the release claims them as supported;
- allow a release-blocking safety/security failure to be waived by a percentage score.

---

# 4. Verification principles

## 4.1 Evidence over command success

The existing SplitOS invariant remains mandatory:

```text
command submitted
!= command accepted
!= operation completed
!= target observed
!= semantic verification passed
!= canonical commit completed
```

Acceptance therefore verifies the final semantic outcome and the relevant durable state.

---

## 4.2 Supported scope is explicit

Every candidate release MUST publish a versioned `ReleaseAcceptanceProfile` that declares at least:

```text
SplitOS release ID
release sequence / security epoch
supported Windows base(s)
supported architectures / editions / languages where applicable
supported Game Client capability matrix
supported hardware/device classes
supported display scenarios
supported Game Profile scenarios
supported update source versions
supported rollback source/target edges
required component matrix revision
required acceptance thresholds
mandatory vs optional capabilities
known unsupported / experimental capabilities
```

A capability not in the supported profile cannot be advertised as production-supported.

---

## 4.3 Optional capability exclusion is not a waiver

Example:

```text
Battle.net adapter
→ EXPERIMENTAL
→ not in production supported capability set
```

Its failing experimental test does not block a release.

But:

```text
Steam GAME_LAUNCH
→ SUPPORTED
```

means the corresponding mandatory acceptance cases MUST pass on all declared supported Steam/client-version cohorts.

It is forbidden to keep `SUPPORTED` while marking a mandatory failed case as “known issue, ship anyway”.

---

## 4.4 Critical invariants are non-waivable

The following classes are release blocking when in release scope:

```text
user data integrity
Windows bootability / usable desktop fallback
canonical state integrity
privilege boundary enforcement
release signature / repository trust
anti-rollback
Recovery Capsule validity
safe update/rollback convergence
secret redaction/export safety
FREE must not be converted to PRO by local/UI evidence
external evidence must not become SplitOS canonical authority
```

No aggregate pass percentage can override them.

---

## 4.5 OPEN remains OPEN until evidence closes it

If a required capability remains `OPEN`, the release must do one of:

```text
1. close it with evidence and update the owning specification;
2. remove that capability from the release supported scope;
3. block the release.
```

Implementation assumptions are not verification evidence.

---

# 5. Verification levels

Cases are categorized by execution level.

```text
L0 STATIC
→ schema / manifest / package / signature / policy inspection

L1 COMPONENT
→ one SplitOS module or adapter in controlled environment

L2 LOCAL_INTEGRATION
→ multiple local processes / Windows APIs / persistence / IPC

L3 SYSTEM
→ installed SplitOS machine, end-to-end behavior

L4 FAULT_INJECTION
→ crash / kill / disconnect / corruption / reboot / power-loss simulation

L5 COMPATIBILITY_MATRIX
→ repeated system verification across declared Windows/hardware/client matrix

L6 RELEASE_ACCEPTANCE
→ immutable candidate artifacts, production-equivalent signing/config, final gate evidence
```

L0/L1 passing never substitutes for required L3-L6 evidence.

---

# 6. Case outcome model

Allowed execution results:

```text
PASS
FAIL
BLOCKED
NOT_APPLICABLE
```

`BLOCKED` means the test could not be executed because its prerequisite/environment/evidence is unavailable. It is not a success.

`NOT_APPLICABLE` is valid only when the case is provably outside the declared `ReleaseAcceptanceProfile`.

No `SKIPPED_AS_PASS` state exists.

---

# 7. Case criticality

Each verification case declares one criticality:

```text
RELEASE_BLOCKING
SCOPE_BLOCKING
NON_BLOCKING_OBSERVATION
```

### RELEASE_BLOCKING

Failure blocks the entire release because a global invariant is violated.

Examples:

```text
invalid signature accepted
FREE gains PRO without entitlement
rollback destroys user profiles
update leaves machine unbootable
Broker accepts arbitrary command execution
```

### SCOPE_BLOCKING

Failure blocks the affected support claim.

Example:

```text
Epic launch fails on client version X
```

Possible resolution:

```text
fix and pass
or
remove version X from supported matrix
```

### NON_BLOCKING_OBSERVATION

Does not prove mandatory product correctness; records useful trend/data only.

Examples:

```text
non-contract performance comparison
experimental Battle.net behavior
future capability prototype
```

---

# 8. Release gates

A production candidate passes the following gate families.

```text
GATE-00  Artifact Identity & Reproducibility
GATE-01  Build & Clean Installation
GATE-02  Identity / FREE / PRO
GATE-03  Runtime / IPC / Persistence
GATE-04  Mode Runtime & Windows Context
GATE-05  Gaming / Profiles / Launcher
GATE-06  Fault Convergence & Recovery
GATE-07  Update / Rollback / Data Preservation
GATE-08  Security / Trust / Supply Chain
GATE-09  Performance / Resource Budgets
GATE-10  Compatibility Matrix
GATE-11  Observability / Privacy / Supportability
GATE-12  Release Evidence & Sign-off
```

All mandatory gates must pass for `PRODUCTION_READY`.

---

# 9. Release verdict

Canonical candidate verdicts:

```text
INCOMPLETE
BLOCKED
READY_FOR_LIMITED_VALIDATION
RELEASE_CANDIDATE
PRODUCTION_READY
REJECTED
```

## `PRODUCTION_READY`

Requires:

```text
all RELEASE_BLOCKING cases = PASS
all SCOPE_BLOCKING cases in declared supported scope = PASS
no required case = BLOCKED
no required release-scope OPEN remains unresolved
support matrix frozen
candidate artifact identities frozen
signing/trust evidence verified
diagnostic/privacy gates passed
release evidence bundle complete
```

---

# 10. Candidate immutability

Acceptance is tied to exact artifacts.

If after acceptance any production artifact changes:

```text
binary
manifest
component matrix
knowledge package
release metadata
policy catalog
recovery authorization
```

then affected evidence becomes stale and must be re-evaluated.

The release candidate is identified by exact hashes/metadata, not by a mutable label such as `1.0-latest`.

---

# 11. Verification evidence model

Each executed case produces a `VerificationResult` conceptually containing:

```text
caseId
caseVersion
releaseId
candidateIdentity
acceptanceProfileId
matrixCellId
executedAtUtc
executor / environment identity
precondition evidence
input fixture identity
result
observed values
assertion results
correlation / transaction IDs where relevant
diagnostic artifact references
failure code if any
retest relationship if any
```

Evidence itself does not alter product state.

---

# 12. Test case contract

A normative acceptance case uses:

```text
Case ID
Purpose
Source requirement/spec
Level
Criticality
Matrix dimensions
Given
When
Then
Required evidence
Failure interpretation
Cleanup / recovery
```

For behavior, `Given / When / Then` is preferred.

Example:

```text
VA-MODE-001

Given
  PRO entitlement valid
  committed mode WORK
  transition state IDLE
  target GAME policy resolvable

When
  user requests GAME

Then
  WORK remains canonical until mandatory target verification passes
  transition journal records action progress
  GAME is committed atomically only after verification
  final committed mode = GAME
  transition terminal outcome = COMPLETED
```

---

# 13. Fault-injection rule

Fault tests are first-class acceptance tests, not “chaos testing later”.

Mandatory fault families include:

```text
Runtime Host termination at transition checkpoints
Broker termination / restart
Windows reboot during update phases
unexpected shutdown / power-loss equivalent
stale mutation lease owner
storage write failure / disk full
projection corruption
canonical DB corruption path
network loss during account/update refresh
device hot-unplug
Game Client handoff without game start
update artifact corruption
Recovery Capsule corruption
clock rollback
expired/revoked signing metadata
```

The expected result is usually safe convergence, not “operation always succeeds”.

---

# 14. Performance threshold rule

Several existing NFR performance limits are still `TBD`.

SPEC-14 therefore defines **how thresholds are bound**, not arbitrary product values.

Every release acceptance profile MUST resolve required numeric values before production qualification, for example:

```text
max RuntimeHost idle CPU
max RuntimeHost idle working set
max Launcher background GPU/CPU
max mode transition duration percentile
max managed game FPS/frame-time regression
max startup latency
max update/recovery time where product requirement exists
```

A missing mandatory threshold means the corresponding performance gate is `BLOCKED`, not automatically passed.

---

# 15. Compatibility rule

Passing once on one developer PC never establishes general support.

Production support is defined by declared matrix cells such as:

```text
Windows base/build
GPU vendor/family/driver cohort
display scenario
controller/input class
Game Client/version cohort
game/config-adapter version
storage/recovery layout
upgrade source release
```

Each claimed supported cell must carry evidence appropriate to its risk.

---

# 16. Verification ownership

Product/System Analysis owns semantic acceptance intent.

Engineering owns implementation-level automated tests and fixtures.

Release/QA owns candidate execution/evidence collection.

Security owns/approves security gate evidence where required.

Compatibility owns supported-matrix decisions.

No test runner becomes authority for product semantics.

---

# 17. Evidence retention

Release acceptance evidence must be retained long enough to answer:

```text
what exact release was approved?
against what support matrix?
with what artifacts/hashes?
which tests passed?
which capabilities were excluded?
which known risks existed?
who approved the release?
```

Exact retention infrastructure is an implementation/process choice, but production release evidence must not exist only on an engineer's workstation.

---

# 18. Relationship to observability

SPEC-13 provides diagnostics that support verification.

Examples:

```text
correlation timeline
ETW trace
security audit
crash dump
diagnostic bundle
```

But:

```text
DiagnosticRecord
!= VerificationResult
!= canonical state
```

Verification consumes diagnostics as evidence and asserts against canonical state owners.

---

# 19. Minimum v1 acceptance families

The A&D handoff requires at least:

```text
Build verification
FREE runtime access
PRO entitlement activation
Work→Game
Game launch
Game exit
Game→Work
Device loss/fallback
Runtime Host/Broker crash
Offline entitlement
Update/reboot/resume
Recovery
Trust boundary abuse tests
Artifact tampering/signature tests
```

SPEC-14 expands these into the complete gate model in this package.

---

# 20. Package artifacts

```text
SPEC-14-Verification-and-Acceptance/
├── README.md
├── Verification Model and Release Gates.md
├── Acceptance Scenario Catalog.md
├── Fault Injection and Recovery Verification.md
├── Security and Trust Verification.md
├── Performance and Resource Verification.md
├── Compatibility and Hardware Matrix.md
├── Build Install Update Acceptance.md
├── Data Privacy and Diagnostics Acceptance.md
├── Release Readiness Evidence and Sign-off.md
├── SPEC-14 Traceability.md
├── verification-pipeline.mmd
├── release-gates.mmd
└── fault-convergence.mmd
```

---

# 21. SPEC decisions

```text
SPEC-DEC-158  Release acceptance is gate-based, not percentage-based.
SPEC-DEC-159  Supported scope is frozen in a versioned ReleaseAcceptanceProfile.
SPEC-DEC-160  Critical safety/security/data/recovery invariants are non-waivable.
SPEC-DEC-161  Optional capability failures are handled by removing the support claim, not by pretending PASS.
SPEC-DEC-162  PASS/FAIL/BLOCKED/NOT_APPLICABLE are the only normative case execution results.
SPEC-DEC-163  Fault injection is mandatory release evidence for state-mutating flows.
SPEC-DEC-164  Acceptance binds to immutable candidate artifact identities.
SPEC-DEC-165  Required numeric performance thresholds are supplied by the release acceptance profile; unresolved TBD blocks the gate.
SPEC-DEC-166  Compatibility is proven per declared support matrix, not by one successful machine.
SPEC-DEC-167  Verification evidence is distinct from canonical product state and from diagnostic logs.
SPEC-DEC-168  Production readiness requires complete release evidence and explicit sign-off.
```

---

# 22. Result

After SPEC-14, Detailed Specification is complete enough to move into implementation planning/delivery slices while preserving one verification chain:

```text
Discovery / Decisions
→ Requirements
→ Analysis & Design
→ SPEC-01..14
→ Implementation
→ Verification Evidence
→ Release Gate
→ Production Release
```

Any implementation evidence that disproves an existing assumption must flow back through the owning source-of-truth layer instead of being hidden behind a test exception.
