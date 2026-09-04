# SPEC-07 — Launch Correlation and Session Evidence

## 1. Purpose

Defines the common process/application observation algorithm used after a Game Client accepts a launch handoff.

This document exists because:

```text
client handoff
!= game process
!= game running
```

and client-specific launchers frequently break simple parent/child-process assumptions.

---

## 2. Ownership

Game Client Adapter owns:

- client-specific correlation rules;
- normalized evidence emitted from those rules.

Game Launch Orchestration owns:

- launch attempt;
- launch observation deadline;
- interpretation of adapter handoff outcome.

Game Session owns:

- canonical session state.

Windows Process Integration (SPEC-06) owns:

- actual process/session/image evidence.

---

## 3. Baseline snapshot

Immediately before external handoff, Runtime captures a bounded process snapshot for the active Windows user session.

The baseline must preserve enough identity to distinguish existing processes from new/replaced processes:

```text
PID
creationTimeUtc
sessionId
normalized image path where readable
```

PID alone is insufficient because Windows can reuse PIDs.

---

## 4. Launch observation window

A single launch attempt creates:

```text
LaunchObservationWindow
{
  launchOperationId
  externalGameIdentity
  handoffUtc
  startDeadlineUtc
  runningDeadlineUtc
  baselineProcessSnapshotId
  observationRulesVersion
}
```

All correlation evidence belongs to this attempt.

---

## 5. Candidate discovery

After handoff, the observer receives process/application changes and periodically reconciles the current snapshot.

Candidate dimensions may include:

```text
created after handoff
not present in baseline by PID+creation identity
same Windows user session
expected image name
image beneath validated install root
expected AUMID/package identity
expected publisher/game launcher pattern
known bootstrap/replacement relationship
foreground/window evidence
```

No single dimension is universally sufficient.

---

## 6. Evidence score is not a magic number

Implementation MAY use an internal scoring helper, but canonical correlation uses named proof sets rather than a hidden numeric threshold.

Good:

```text
ProofSet STEAM_INSTALL_ROOT_V1
ProofSet MICROSOFT_AUMID_V1
ProofSet CURATED_EXECUTABLE_V2
```

Bad:

```text
if score > 73 then running
```

without explainable evidence.

---

## 7. Generic proof sets

### PS-01 — Curated executable

```text
expected executable identity
+
validated install root
+
same user session
+
created/activated after launch handoff
```

Evidence level: `STRONG`.

### PS-02 — Packaged/AUMID activation

```text
expected AUMID/PFN
+
Windows activation/process identity correlation
+
current user session
```

Evidence level: `STRONG`.

### PS-03 — Install-root generic

```text
new process
+
same session
+
path under validated game install root
+
not known client/helper process
+
stable for minimum interval
```

Evidence level: `MEDIUM`.

### PS-04 — Window/name only

```text
new foreground process
+
name resembles game
```

Evidence level: `WEAK` and insufficient alone.

---

## 8. Process roles

Every correlated process has one role:

```text
CLIENT
BOOTSTRAP
PUBLISHER_LAUNCHER
GAME_PRIMARY
GAME_SECONDARY
HELPER
UNKNOWN_CANDIDATE
```

Only adapter/per-game compatibility knowledge can promote a process from candidate to known role.

---

## 9. Bootstrap replacement

Typical pattern:

```text
bootstrap starts
↓
bootstrap launches real game
↓
bootstrap exits
```

If compatibility rules permit this transition:

```text
BOOTSTRAP exit
+
valid GAME_PRIMARY appears in replacement window
→ session continues
```

The bootstrap exit is not game exit.

---

## 10. Publisher launcher chain

Possible flow:

```text
Steam/Epic handoff
→ publisher launcher
→ game
```

The publisher launcher may remain open or exit.

Game Session running confirmation requires eventual game proof unless the specific title contract explicitly defines the publisher launcher itself as the managed foreground application.

---

## 11. Already-running baseline

Before submitting launch, Game Launch may check for an existing strongly correlated process set.

If found:

```text
ALREADY_RUNNING_CONFIRMED
```

and policy decides whether to attach/focus/reject.

Weak same-name matching cannot produce `ALREADY_RUNNING_CONFIRMED`.

---

## 12. STARTING confirmation

`GAME_STARTING_CONFIRMED` can be emitted when:

- at least one strong/medium launch candidate exists;
- candidate belongs to expected game context;
- evidence is no longer only client-process activity.

This state permits Game Launcher to show launching progress without claiming the game is already running.

---

## 13. RUNNING confirmation

A game becomes `RUNNING_CONFIRMED` only after:

```text
supported proof set satisfied
+
minimum stability/readiness condition
+
no unresolved ambiguity
```

Readiness may be:

- process stable for minimum interval;
- expected game primary role established;
- application activation completed;
- optional expected top-level window observed.

A top-level window is strengthening evidence, not a universal requirement.

---

## 14. Anti-false-positive rule

The observer MUST reject correlation where only evidence is:

```text
new foreground process
or
same executable filename somewhere else
or
Steam/Epic/Battle.net/Xbox app itself opened
```

This prevents a browser/chat/client window from becoming `GAME_RUNNING`.

---

## 15. Process path validation

When install-root correlation is used:

```text
normalized process image path
↓
canonical path relationship check
↓
within validated install root?
```

String prefix comparison alone is insufficient.

Path normalization must account for separators, casing semantics, reparse/symlink behavior where relevant, and inaccessible paths.

---

## 16. Process access failure

If Windows denies image-path access for a candidate:

```text
unknown evidence
```

not:

```text
candidate automatically trusted
```

Adapter may use other supported identity evidence or report correlation unavailable.

---

## 17. Packaged application nuance

For packaged/AUMID launches, process image location may be less useful than application/package identity.

Therefore the Microsoft adapter may confirm using package/AUMID proof even if raw executable path is not the primary identity.

---

## 18. Stability windows

Time values are configuration/compatibility data.

Conceptual values:

```text
candidateStabilityWindow
replacementWindow
exitGraceWindow
startDeadline
runningDeadline
```

SPEC-07 does not hard-code one duration for every game.

SPEC-14/performance verification will establish default bounds.

---

## 19. Start timeout

When deadline expires without sufficient proof:

```text
GAME_PROCESS_NOT_CONFIRMED
```

The adapter/Game Launch may separately report any proven client interaction.

Timeout never means:

```text
kill client/game
switch mode automatically
```

Normal result while in GAME:

```text
return/remain in Game Launcher
```

---

## 20. Runtime Host crash during observation

On same-logon Runtime restart:

1. reload active Game Session/launch context if persisted;
2. rebuild fresh Windows process evidence;
3. compare against persisted external game/client identity;
4. search for a strong existing correlated game process;
5. reattach if proven;
6. otherwise converge to failed/inactive session according to Game Session owner.

Blind relaunch is forbidden until reconciliation finishes.

---

## 21. Process replacement after running

Some games replace executable during startup/update/relaunch.

Replacement can be accepted only if:

- it matches a release-owned permitted replacement rule;
- it appears in the allowed window/context;
- old and new evidence are correlated to the same launch/game identity.

Otherwise:

```text
CORRELATION_LOST
```

and session owner decides recovery/exit behavior.

---

## 22. Exit candidate

When a required `GAME_PRIMARY` exits:

```text
EXIT_CANDIDATE
```

is emitted first.

The observer waits through the exit/replacement grace window.

---

## 23. Exit confirmation

`GAME_EXITED_CONFIRMED` requires:

```text
no required correlated game processes
+
no allowed replacement appears
+
current evidence snapshot fresh
```

External client helpers are excluded.

---

## 24. Crash vs normal exit

Generic process evidence often cannot distinguish clean exit from crash.

Therefore SPEC-07 emits:

```text
GAME_EXITED_CONFIRMED
```

plus optional diagnostic exit code where readable.

Whether UX says “game crashed” requires stronger evidence and belongs Game Session/UX behavior.

---

## 25. Multiple managed games

v1 policy remains:

```text
one managed foreground game session at a time
```

If a second strongly correlated supported game already runs when another launch is requested:

```text
CONFLICTING_GAME_RUNNING
```

Game Launch policy decides whether to block/ask user.

SPEC-07 does not kill either game automatically.

---

## 26. User manually launches another game

A game started outside the current SplitOS managed launch is external process evidence.

SplitOS MUST NOT automatically steal it into the current Game Session from weak heuristics.

Future passive-session adoption would require a separate product decision/spec.

---

## 27. Window evidence

Window title/class/foreground state may strengthen correlation but is unstable/localized.

Rules:

- title text alone is WEAK;
- window HWND linked to a strongly/medium correlated process may strengthen readiness;
- foreground status is UX evidence, not identity;
- no UI automation/injection is required.

---

## 28. Observability record

Each correlation decision SHOULD emit:

```text
launchOperationId
clientType
externalGameIdentity
proofSetId
evidenceLevel
candidate process identities
accepted/rejected reason code
snapshot timestamp
```

This is essential for debugging false positives/negatives.

---

## 29. Verification cases

```text
V-CORR-001 Steam client opens but no game → no false running
V-CORR-002 browser becomes foreground after handoff → ignored
V-CORR-003 expected executable under install root → running
V-CORR-004 same filename outside install root → rejected
V-CORR-005 PID reuse distinguished by creation time
V-CORR-006 bootstrap replacement supported
V-CORR-007 unknown replacement becomes correlation lost
V-CORR-008 packaged AUMID activation proof
V-CORR-009 Runtime crash and reattach to already-running game
V-CORR-010 primary exits then valid replacement appears → no exit
V-CORR-011 all correlated processes exit → exit confirmed
V-CORR-012 client remains running after game exit → session exits
V-CORR-013 weak foreground/timing evidence insufficient
V-CORR-014 conflicting second game detected without force kill
```

---

## 30. Result

The launch observer provides explainable evidence rather than a fragile process-parent heuristic:

```text
handoff
→ candidate discovery
→ proof set
→ starting/running evidence
→ replacement tracking
→ exit confirmation
```

Game Session remains the canonical state owner throughout.
