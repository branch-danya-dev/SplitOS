# SPEC-05 — Blocker and Decision Engine

## 1. Purpose

Defines how Mode Runtime discovers, classifies, presents and resolves conditions that may prevent a safe mode operation.

The engine aggregates evidence. It does not invent facts that a provider cannot actually prove.

---

## 2. Provider model

Each blocker provider implements a bounded contract:

```text
Inspect(context) → BlockerObservation[]
Resolve(blocker, approvedDecision?) → ResolutionResult
Recheck(blocker) → BlockerObservation | RESOLVED
```

Provider identity is release-defined and versioned.

Initial provider families:

```text
RuntimeAccessProvider
MajorMutationProvider
RecoveryStateProvider
ApplicationProcessProvider
AppSpecificSafetyProvider
GameSessionProvider
DisplayAvailabilityProvider
InputAvailabilityProvider
SystemContextReadinessProvider
LauncherReadinessProvider
```

Device-specific mechanisms belong to SPEC-06.

---

## 3. Blocker classes

### 3.1 NON_BLOCKING

Evidence exists but does not prevent transition.

Example:

```text
background Shared App allowed by target policy
```

### 3.2 AUTO_RESOLVABLE

Runtime has an owner-approved low-risk action that may resolve the condition without user confirmation.

Examples may include:

- stop/deactivate a release-managed helper explicitly marked auto-resolvable;
- move a SplitOS-owned helper to target lifecycle state;
- refresh stale device evidence.

`AUTO_RESOLVABLE` MUST NOT be used merely because automation is technically possible.

### 3.3 USER_DECISION_REQUIRED

Continuation is potentially disruptive and requires an explicit user choice.

Examples:

- active managed game when switching GAME→WORK;
- known work-critical process whose policy requires confirmation before close;
- app-specific provider proves unsaved work and supports explicit choices.

### 3.4 HARD_BLOCK

Current conditions make safe target activation impossible.

Examples:

- managed runtime target authorization absent;
- Recovery owns the machine;
- mandatory target display/input context cannot be resolved and no approved fallback exists;
- release/schema incompatibility;
- target launcher readiness cannot be established for normal GAME commit.

---

## 4. Observation shape

A blocker observation contains:

```text
blockerId
providerId
blockerCode
blockerClass
subjectRef
messageKey
evidenceTimestamp
evidenceDigest
freshnessDeadline?
decisionSchemaVersion?
decisionOptions[]
```

`messageKey` references product-localized UX text; providers do not send arbitrary untrusted human-readable external strings directly to privileged/logical decisions.

---

## 5. Evidence discipline

Examples of valid inference boundaries:

```text
process running
→ process presence evidence

process running
!= unsaved document

Steam process running
!= active managed game session

monitor enumerated
!= requested mode/refresh actually usable
```

A blocker provider MUST document what its evidence actually proves.

---

## 6. Application Process Provider

Generic process/application inspection may use release-defined application identities/classification.

It may report:

```text
application/process present
classification
known lifecycle policy
known managed ownership
```

It MUST NOT claim application-internal document/save state without an app-specific supported integration.

### 6.1 Generic policy examples

```text
WORK_CRITICAL + running
→ USER_DECISION_REQUIRED

SHARED + allowed in target
→ NON_BLOCKING

SplitOS-managed helper + auto-stop policy
→ AUTO_RESOLVABLE
```

Exact app catalog belongs Application Lifecycle / future implementation configuration.

---

## 7. App-specific safety providers

An app-specific provider may expose richer evidence only when supported and tested.

Possible semantic result:

```text
SAFE_TO_CLOSE
UNSAVED_STATE_PRESENT
BUSY_OPERATION
UNKNOWN
```

`UNKNOWN` MUST NOT be silently treated as `SAFE_TO_CLOSE` when the operation is potentially lossy.

Provider failure normally degrades to a conservative policy defined for that app class.

---

## 8. Game Session Provider

For GAME→WORK:

```text
GAME_RUNNING / GAME_STARTING
→ USER_DECISION_REQUIRED unless a stricter policy blocks transition

LAUNCHER / INACTIVE
→ no game-session blocker
```

Approved decision examples:

```text
CLOSE_GAME_AND_CONTINUE
CANCEL_SWITCH
```

The engine MUST wait for actual game-exit evidence before considering `CLOSE_GAME_AND_CONTINUE` resolved.

A close request accepted by a client/process is not sufficient.

---

## 9. Runtime Access Provider

For target WORK/GAME activation/switch:

```text
runtime.managed_modes capability absent
→ HARD_BLOCK
```

For DEACTIVATE to NONE:

```text
premium capability absent
→ not a blocker
```

because deactivation is the required safe convergence path.

---

## 10. Device availability providers

Display/input/audio providers return semantic readiness, not raw API errors.

Example:

```text
TARGET_AVAILABLE
TARGET_AVAILABLE_WITH_FALLBACK
TARGET_UNAVAILABLE
EVIDENCE_STALE
```

Fallback may proceed automatically only if the effective policy explicitly permits that fallback class.

Otherwise:

```text
TARGET_AVAILABLE_WITH_FALLBACK
→ USER_DECISION_REQUIRED or HARD_BLOCK
```

Exact fallback rules are completed in SPEC-06/SPEC-08.

---

## 11. Decision options

Each `USER_DECISION_REQUIRED` blocker defines a closed option set.

Example:

```text
blockerCode: ACTIVE_GAME
options:
- CLOSE_AND_CONTINUE
- CANCEL
```

Forbidden design:

```text
free-form user instruction
→ arbitrary system command
```

A decision code is interpreted only by the owning provider/transition engine.

---

## 12. User decision persistence

When a user chooses an option, Runtime persists:

```text
blockerId
selectedDecisionCode
decisionUtc
operationId
correlationId
```

before executing a disruptive resolution action where durability matters.

The persisted choice applies only to that transition/blocker instance.

It MUST NOT silently become a permanent global preference unless a separate explicit settings flow does so.

---

## 13. Stale evidence

Blocker evidence may expire.

Before entering `APPLYING`, the engine MUST revalidate any blocker whose evidence freshness deadline has passed.

Examples:

- monitor disconnected after initial inspection;
- game exited while prompt was open;
- process closed itself;
- entitlement refreshed;
- Recovery state appeared.

Stale blocker rows are marked `STALE`; they are not deleted in a way that hides transition history.

---

## 14. Resolution loop

Canonical loop:

```text
INSPECT
↓
aggregate blockers
↓
HARD_BLOCK? → BLOCKED / CANCEL
↓
USER_DECISION? → AWAITING_USER
↓
AUTO_RESOLVABLE? → RESOLVING
↓
perform bounded resolutions
↓
RECHECK
↓
no blocking conditions
→ build target action plan
```

The engine MUST bound repeated re-inspection; it cannot spin forever if a condition continually changes.

Exact retry counts/timeouts may be provider-specific, but indefinite hidden retries are prohibited.

---

## 15. Multiple blockers

UI SHOULD present blockers as one coherent decision surface where practical, but decisions remain independently recorded per blocker.

Ordering priority:

```text
HARD_BLOCK
→ user data / running session risk
→ required device/runtime readiness
→ convenience/non-blocking information
```

If a HARD_BLOCK already makes continuation impossible, Runtime MAY avoid prompting for unrelated lower-priority decisions.

---

## 16. Cancel semantics

Any user decision `CANCEL` during pre-mutation inspection ends the operation:

```text
CANCELLED
```

If earlier auto-resolution already caused target-relevant mutations, cancellation uses rollback rules instead of pretending nothing changed.

---

## 17. Security / privacy

Blocker persistence and logs MUST NOT store:

- document contents;
- account tokens;
- arbitrary window text where not necessary;
- sensitive command-line arguments by default.

Prefer stable app/process identity, blocker code and bounded metadata.

---

## 18. Acceptance criteria

- every blocker has a versioned provider and code;
- blocker class has deterministic semantics;
- generic process evidence never becomes fake unsaved-work evidence;
- user decisions come from a closed option set;
- decisions are transition-scoped;
- stale evidence is revalidated;
- game-close confirmation waits for actual exit evidence;
- fallback requires policy authorization;
- hard blockers cannot be silently waived;
- hidden infinite resolution loops are prohibited.
