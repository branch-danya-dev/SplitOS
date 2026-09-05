# SPEC-14 — Verification Model and Release Gates

## 1. Purpose

Defines the normative verification model used to decide whether a SplitOS release candidate is acceptable for production support.

This document answers:

```text
what is verified?
against what support claim?
what counts as evidence?
what blocks a release?
what can be excluded from scope?
when does evidence become stale?
who signs off?
```

---

# 2. Verification object model

Conceptual entities:

```text
ReleaseCandidate
ReleaseAcceptanceProfile
VerificationCase
VerificationExecution
VerificationResult
MatrixCell
ReleaseGate
ReleaseGateResult
ReleaseReadinessRecord
```

They are verification/process artifacts, not runtime canonical state.

---

## 2.1 ReleaseCandidate

Identifies one immutable candidate composition:

```text
releaseId
releaseSequence
securityEpoch
Windows base constraints
BuildReceipt identity
BuildManifest digest
ComponentMatrix digest
SplitOS package/artifact digests
release metadata identity
knowledge package versions
recovery authorization set
```

If an identity-bearing artifact changes, a new candidate identity is required.

---

## 2.2 ReleaseAcceptanceProfile

Defines what the release claims to support and the thresholds against which it is judged.

Minimum fields:

```text
acceptanceProfileId
profileVersion
releaseCandidateId
supportedWindowsMatrix
supportedHardwareMatrix
supportedGameClientMatrix
supportedGameSet/config-adapter matrix
supportedUpdateSources
supportedRecoveryEdges
requiredCapabilities
optionalCapabilities
experimentalCapabilities
performanceThresholds
resourceThresholds
retentionThresholds where release-qualified
requiredVerificationSuites
```

The profile is frozen before final L6 release acceptance execution.

---

## 2.3 VerificationCase

A stable versioned test contract.

Minimum metadata:

```text
caseId
caseVersion
title
sourceRefs[]
level
criticality
requiredCapabilities[]
applicability predicate
Given
When
Then
requiredEvidence[]
```

Case IDs are immutable semantic identities. If expected semantics materially change, increment case version or create a new case.

---

## 2.4 VerificationExecution

One attempt of one case against one exact candidate/matrix cell.

```text
executionId
caseId/version
candidateId
matrixCellId
startedAtUtc
endedAtUtc
environmentIdentity
fixtureIdentity
result
observations
assertions
evidenceRefs
failureCode
```

---

# 3. Release gate families

## GATE-00 — Artifact Identity & Reproducibility

Purpose:

```text
prove the object under test is exactly the candidate intended for release
```

Mandatory assertions include:

- candidate hashes match Release Envelope / trusted metadata;
- BuildReceipt points to the exact BuildManifest/Component Matrix/package set;
- no unsigned/untracked candidate artifact is introduced after acceptance start;
- test environment records actual Windows/hardware/client versions;
- production signing and repository metadata are verified in release-equivalent path.

Failure is globally release blocking.

---

## GATE-01 — Build & Clean Installation

Verifies:

- supported Windows source accepted;
- unsupported source rejected;
- typed manifest operations only;
- all mandatory postconditions verified;
- prepared image boots;
- Windows Setup/OOBE completes;
- Windows user creation works;
- SplitOS runtime is provisioned after first sign-in;
- expected REMOVE/DISABLE/MODE_MANAGED/KEEP states match accepted Component Matrix;
- required Windows servicing/recovery substrate remains functional.

---

## GATE-02 — Identity / FREE / PRO

Verifies:

```text
Windows Account != SplitOS Account
SplitOS Account != Entitlement
FREE != PRO
```

Mandatory cases:

- Windows remains usable before/without SplitOS account completion where defined;
- first-run SplitOS account association works per Windows user;
- FREE keeps managed runtime disabled and mode `NONE`;
- backend outage never blocks Windows sign-in;
- browser callback does not itself grant PRO;
- valid backend entitlement enables appropriate managed capabilities;
- expired/revoked/invalid entitlement cannot continue granting new premium operations;
- offline assertion follows bound TTL/device/account/clock rules.

---

## GATE-03 — Runtime / IPC / Persistence

Verifies:

- exactly one Broker service per machine;
- one Runtime Host per eligible session;
- active physical console ownership enforcement;
- Manager/Launcher cannot directly use privileged Broker contract;
- Named Pipe caller/session/process validation;
- capability allowlist;
- idempotency behavior;
- stale fencing denial;
- canonical DB durability expectations;
- machine DB ordinary-user write protection;
- user DB user isolation;
- migration/corruption behavior.

---

## GATE-04 — Mode Runtime & Windows Context

Mandatory flows:

```text
NONE → WORK
NONE → GAME
WORK → GAME
GAME → WORK
WORK/GAME → NONE
```

Verifies:

- source mode remains canonical until commit;
- blockers handled according to class;
- user cancellation preserves source mode;
- target policy resolution uses fresh device evidence;
- stale snapshot is rejected/re-resolved;
- Windows apply success is followed by read-back verification;
- commit is atomic with transition durable state;
- rollback/recovery semantics match failure class;
- display/power/services/input/hardware integration expectations hold.

---

## GATE-05 — Gaming / Profiles / Launcher

Verifies:

- Game Client capability matrix claims;
- installation/library projection freshness semantics;
- launch handoff != GAME_RUNNING;
- strong process/application evidence confirms running state;
- exit evidence returns Launcher while GAME remains committed;
- profile selection is deterministic;
- user field locks outrank optimizer recommendation where valid;
- stale hardware context invalidates resolved profile;
- config adapter conflict/read-back rules;
- Launcher READY_PRECOMMIT/ACTIVE/background/restore lifecycle;
- controller navigation focus contract;
- Shared Apps max 3 and presentation degradation behavior.

---

## GATE-06 — Fault Convergence & Recovery

Verifies safe convergence after fault injection.

Critical expected invariants:

```text
no guessed canonical target after ambiguous crash
no dual active mode
no infinite rollback loop
rollback failure escalates to Recovery
Windows usability outranks premium UX
reboot/startup distinguishes committed vs uncommitted transaction
```

Mandatory fault points are defined in `Fault Injection and Recovery Verification.md`.

---

## GATE-07 — Update / Rollback / Data Preservation

Verifies:

- SplitOS feed only controls SplitOS-owned payload;
- Windows servicing lane compatibility gate;
- target artifacts fully downloaded/verified before activation;
- previous-release Recovery Capsule verified before mutation;
- update journal survives reboot;
- commit identity changes only after target verification;
- rollback requires authorized exact recovery edge;
- user canonical data remains current across software rollback;
- at least previous-release schema compatibility/bridge works;
- recovery capsule cannot substitute for full device backup.

---

## GATE-08 — Security / Trust / Supply Chain

Verifies:

- TUF root/targets/snapshot/timestamp validation;
- metadata expiration/freeze/rollback protections;
- release/recovery delegation constraints;
- Authenticode publisher validation;
- artifact hash binding;
- anti-rollback floors;
- RecoveryAuthorization exact edge/context;
- compromised/stale/revoked key behavior fixtures;
- Broker abuse attempts;
- malformed/untrusted external metadata isolation;
- secret storage/redaction expectations.

No failure in a release-scoped security invariant is waivable.

---

## GATE-09 — Performance / Resource Budgets

Uses exact numeric thresholds from `ReleaseAcceptanceProfile`.

Required dimensions may include:

```text
idle RuntimeHost CPU
idle RuntimeHost RAM
Launcher active/background CPU/GPU/RAM
mode transition latency distribution
game launch added latency
managed gaming frame-time/FPS regression
telemetry overhead
disk growth / log rotation
```

If a mandatory threshold remains undefined, the gate is BLOCKED.

---

## GATE-10 — Compatibility Matrix

Verifies every production support claim across required matrix cohorts.

Matrix dimensions include at least where relevant:

```text
Windows base / patch level
GPU vendor + driver cohort
display topology
input/controller class
audio endpoint class
Game Client/version
supported game/config-adapter version
upgrade source release
storage/recovery configuration
```

A cell can be removed from production support before release; it cannot remain claimed supported with failed mandatory evidence.

---

## GATE-11 — Observability / Privacy / Supportability

Verifies:

- required operational events exist;
- critical security audit decisions are captured;
- correlation reconstructs target flows;
- diagnostics are separate from canonical state;
- WER/ETW capture works when requested;
- bundle is bounded and incident-specific;
- secret-forbidden fields do not appear;
- redaction failure blocks export;
- retention/space-pressure rules respect canonical/recovery priority;
- no implicit remote telemetry occurs.

---

## GATE-12 — Release Evidence & Sign-off

Checks completeness of evidence rather than product runtime behavior.

Mandatory:

```text
candidate identity frozen
acceptance profile frozen
all mandatory gates evaluated
all required matrix cells evaluated
all RELEASE_BLOCKING results PASS
all in-scope SCOPE_BLOCKING results PASS
known unsupported/experimental scope explicit
open issues linked
release notes aligned with support matrix
security/compatibility/release owners sign off
```

---

# 4. Gate result states

```text
PASS
FAIL
BLOCKED
NOT_APPLICABLE
```

A gate `PASS` requires every mandatory child condition to pass.

A child result `NOT_APPLICABLE` requires a machine-readable/traceable applicability reason derived from the frozen acceptance profile.

---

# 5. Scope change during verification

If verification discovers a failure in a capability that can be removed from production support without violating a higher-level requirement:

```text
failure
↓
compatibility/product decision
↓
update ReleaseAcceptanceProfile
↓
new profile version
↓
re-run affected gate selection
↓
release notes/support matrix updated
```

This is not a test waiver. The release claim changed.

---

# 6. Evidence freshness

Evidence becomes stale when one of its material inputs changes.

Examples:

```text
candidate artifact digest changed
BuildManifest changed
Component Matrix changed
Windows build/patch changed
GPU driver cohort changed
Game Client version changed
config-adapter knowledge changed
security trust metadata changed
acceptance threshold changed
```

Stale evidence cannot be silently reused.

---

# 7. Retest policy

A failed execution followed by a fix produces a new `VerificationExecution`.

Previous failed evidence remains preserved for traceability.

Result aggregation uses the latest valid execution for the exact case/candidate/matrix cell, while retaining history.

---

# 8. Environment identity

System/compatibility evidence MUST record enough environment identity to reproduce the result:

```text
Windows edition/build/patch
SplitOS release candidate ID
CPU/GPU/RAM/storage class
GPU/critical driver versions
display topology/capabilities
input devices where relevant
Game Client/version
external game build where available
network mode if relevant
power source/config if material
```

Do not record unnecessary personal information.

---

# 9. Production-equivalent verification

Final L6 tests must use production-equivalent:

```text
signed binaries
release metadata/trust roots
Broker service identity/ACL
installer/builder output
update/recovery packaging
security policy
```

A debug build with disabled signature/caller validation cannot qualify production behavior.

---

# 10. Negative testing requirement

Every trust/authority boundary requires at least one negative acceptance case.

Examples:

```text
UI → Broker direct attempt rejected
other Windows session → machine mutation rejected
expired entitlement → premium request rejected
forged checkout callback → no PRO grant
old signed release → normal downgrade denied
corrupt artifact → update denied
untrusted client metadata → no privileged execution
stale fence token → denied
secret-bearing diagnostic field → export blocked/redacted
```

---

# 11. No single test owns truth

A verification test may observe canonical state, actual Windows evidence and diagnostic logs.

It must distinguish:

```text
expected intent
canonical SplitOS state
actual external state
diagnostic evidence
```

A log line cannot satisfy an assertion that specifically requires canonical DB state or Windows read-back.

---

# 12. Gate summary

```text
Candidate identity
↓ GATE-00
Build/install
↓ GATE-01
Identity/FREE/PRO
↓ GATE-02
Runtime/IPC/data
↓ GATE-03
Mode/Windows context
↓ GATE-04
Gaming UX
↓ GATE-05
Fault convergence
↓ GATE-06
Update/rollback
↓ GATE-07
Security/trust
↓ GATE-08
Performance
↓ GATE-09
Compatibility
↓ GATE-10
Observability/privacy
↓ GATE-11
Evidence/sign-off
↓ GATE-12
PRODUCTION_READY
```
