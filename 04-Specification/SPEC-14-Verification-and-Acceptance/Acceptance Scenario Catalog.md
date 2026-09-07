# SPEC-14 — Acceptance Scenario Catalog

## 1. Purpose

Defines the minimum end-to-end acceptance scenarios that every SplitOS v1 release candidate must evaluate when the corresponding capability is in production scope.

The catalog is intentionally semantic. Implementation repositories may split one scenario into many automated tests, but may not weaken the expected outcome.

---

# 2. ID convention

```text
VA-BUILD-xxx
VA-INSTALL-xxx
VA-IDENTITY-xxx
VA-ENT-xxx
VA-IPC-xxx
VA-DATA-xxx
VA-MODE-xxx
VA-WIN-xxx
VA-GAME-xxx
VA-PROFILE-xxx
VA-LAUNCHER-xxx
VA-SHARED-xxx
VA-UPDATE-xxx
VA-RECOVERY-xxx
VA-SEC-xxx
VA-OBS-xxx
```

---

# 3. Build / install

## VA-BUILD-001 — Supported source produces verified baseline

**Criticality:** RELEASE_BLOCKING

**Given**

- source matches release `SourceConstraint`;
- exact BuildManifest/Component Matrix/package set available;

**When**

- Builder prepares installation media;

**Then**

- every mandatory typed operation verifies its postcondition;
- BuildReceipt binds exact source/manifest/matrix/package identities;
- output artifact passes final verification;
- candidate is marked build-instance verified.

---

## VA-BUILD-002 — Unsupported Windows source rejected

**Criticality:** RELEASE_BLOCKING

**Given** unsupported edition/build/architecture/source identity.

**When** Builder validates input.

**Then** no production image is emitted as supported SplitOS baseline.

---

## VA-BUILD-003 — Mandatory transform verification failure fails build

**Given** one required removal/disable/package action cannot reach verified target.

**Then** image is discarded/failed; success is not inferred from partial DISM success.

---

## VA-INSTALL-001 — Clean installation / OOBE / first sign-in

**Given** verified prepared media.

**When** installed on supported hardware.

**Then**

```text
Windows Setup completes
→ Windows OOBE completes
→ Windows user created
→ Windows sign-in succeeds
→ RuntimeHost starts
→ SplitOS First Run appears/executes
```

No SplitOS account credential is required to complete Windows OOBE.

---

# 4. Identity / FREE / PRO

## VA-IDENTITY-001 — SplitOS account is not Windows login principal

Create two Windows users and independently associate different SplitOS Accounts.

Expected:

```text
Windows User A ↔ SplitOS Account A
Windows User B ↔ SplitOS Account B
```

One account association does not replace Windows authentication or automatically leak to the other user.

---

## VA-IDENTITY-002 — Backend unavailable does not brick Windows

**Given** account backend unreachable during first SplitOS run.

**Then** Windows Desktop remains usable; onboarding may remain incomplete; false PRO is not granted.

---

## VA-ENT-001 — FREE stable state

**Given** valid SplitOS Account with no PRO entitlement.

**Then**

```text
ManagedRuntime = DISABLED
OperationalMode = NONE
Windows Desktop usable
```

Game/Work premium transition requests are rejected/redirected to upgrade semantics.

---

## VA-ENT-002 — Checkout callback alone cannot grant PRO

Simulate browser/custom-URI return without backend entitlement update.

Expected: Runtime performs entitlement refresh; state stays FREE if backend still reports FREE.

---

## VA-ENT-003 — Backend-confirmed PRO activates managed runtime

**Given** backend entitlement contains required PRO capability.

**Then** managed runtime access becomes enabled and a valid `NONE → WORK|GAME` activation may be requested.

---

## VA-ENT-004 — Expired offline assertion

**Given** no backend and offline assertion past permitted validity.

**Then** premium capability is not accepted merely from cached local flag; Windows remains usable.

---

## VA-ENT-005 — Clock rollback handling

Move system clock materially behind trusted entitlement time context.

Expected: assertion freshness becomes invalid/indeterminate according to SPEC-04; no false extension of PRO.

---

# 5. IPC / Broker / session ownership

## VA-IPC-001 — UI cannot directly invoke Broker capability

Attempt Broker connection from Manager/GameLauncher process.

Expected: denied; no privileged mutation occurs.

---

## VA-IPC-002 — Non-console session cannot mutate machine mode

**Given** multiple signed-in Windows sessions.

Attempt machine-wide mode mutation from non-active physical console session.

Expected: `SESSION_NOT_CONTROL_OWNER`/equivalent deny; current machine mode unchanged.

---

## VA-IPC-003 — Stale fencing token rejected

Create lease generation N, supersede it with N+1, then replay a Broker mutation using N.

Expected: no mutation; security audit records stale-owner denial.

---

## VA-IPC-004 — Arbitrary privileged primitives absent

Attempt to invoke or discover unsupported generic capabilities such as command execution/arbitrary registry/service/path operations.

Expected: no such production contract exists; malformed/unknown capability denied.

---

# 6. Persistence

## VA-DATA-001 — Mode commit atomicity

At final transition commit, verify durable committed mode and transition commit marker change atomically.

There must be no recoverable durable state where one says target committed while the other unambiguously says source remains canonical.

---

## VA-DATA-002 — Ordinary user cannot write machine canonical DB

Attempt direct modification of `%ProgramData%\SplitOS\Data\machine.db` as ordinary interactive user.

Expected: denied by boundary; no canonical change.

---

## VA-DATA-003 — User data isolation

Windows User B cannot read/write Windows User A's protected SplitOS per-user data through normal permissions/product APIs.

---

## VA-DATA-004 — Projection cache deletion is recoverable

Delete/rebuild `projection.db` under supported conditions.

Expected: canonical profiles/mode/account/release state unchanged; external projections reconcile again.

---

# 7. Mode activation and switching

## VA-MODE-001 — Activate WORK

**Given** PRO, committed mode NONE, transition IDLE.

**When** user selects WORK.

**Then** BASE→WORK policy applies, mandatory actual state verifies, mode atomically commits WORK.

---

## VA-MODE-002 — Activate GAME

Same as above for GAME, including Launcher `READY_PRECOMMIT` requirement.

---

## VA-MODE-003 — Work→Game success

**Given** committed WORK.

**Then**

```text
request GAME
→ inspect blockers
→ resolve GAME target
→ apply
→ read actual state
→ verify
→ commit GAME
```

WORK remains canonical until target verification passes.

---

## VA-MODE-004 — Work→Game user cancellation

Introduce `USER_DECISION_REQUIRED` blocker and choose cancel.

Expected:

```text
CommittedMode = WORK
terminal = CANCELLED
no GAME commit
```

---

## VA-MODE-005 — Target verification fails before commit

Make one mandatory GAME predicate fail after partial apply.

Expected: no GAME commit; rollback/source-safe convergence or Recovery according to failure policy.

---

## VA-MODE-006 — Game→Work with active game

Request WORK while managed game is running.

Expected user-decision flow; cancellation keeps GAME. If close approved, transition does not proceed until actual game exit evidence is confirmed.

---

## VA-MODE-007 — PRO loss deactivation

Lose entitlement while in WORK/GAME.

Expected safe session handling, then controlled `DEACTIVATE → BASE → NONE`; no immediate destructive process kill solely due entitlement expiry.

---

# 8. Windows context

## VA-WIN-001 — Display read-back required

Force Windows to accept a different effective display mode than requested where possible.

Expected: technical API success does not satisfy `DISPLAY_TARGET_REACHED`; transition follows failure/fallback policy.

---

## VA-WIN-002 — Stale display snapshot rejected

Resolve display target at generation N, disconnect/reconnect/change topology to N+1 before apply.

Expected: stale target not applied blindly; re-resolution or transition abort.

---

## VA-WIN-003 — Temporary mode display change does not persist normal profile to Windows database

Verify normal Work/Game transitions do not use persistent Windows display topology write as default and do not unexpectedly alter Windows persistent display configuration after SplitOS exit/reboot.

---

## VA-WIN-004 — Power target verified

PowerSetActiveScheme success must be followed by read-back of expected scheme.

---

## VA-WIN-005 — Managed service final state verified

Start/stop through allowlisted ManagedServiceId, then verify final SCM state rather than command return alone.

---

## VA-WIN-006 — Unsupported automatic default audio setter degrades safely

Where no supported setter exists, profile requiring different default endpoint produces user-mediated Sound Settings path; no undocumented production mechanism silently executes.

---

# 9. Game Client / launch / exit

## VA-GAME-001 — Steam launch handoff is not running proof

After accepted `steam://run/<appid>` handoff, withhold/deny game process start.

Expected: session stays starting/pending, eventually failure/launcher; never `GAME_RUNNING` from handoff alone.

---

## VA-GAME-002 — Epic launch using supported protocol

For a supported Epic matrix cell, validate documented protocol identity and confirm game running only from independent process/application evidence.

---

## VA-GAME-003 — Microsoft Gaming app activation

For supported registered package/AUMID title, use app activation path and confirm session independently.

Direct arbitrary game EXE launch is not used as generic fallback.

---

## VA-GAME-004 — Unsupported/experimental client does not masquerade as supported

Battle.net or another experimental adapter not present in supported profile must be shown/treated as experimental/unsupported according to product policy; failed experiment does not corrupt other adapters.

---

## VA-GAME-005 — Bootstrap process replacement

Exercise launcher/bootstrap→primary game process replacement.

Expected: bootstrap exit does not falsely indicate game exit while correlated primary game remains.

---

## VA-GAME-006 — Confirmed game exit returns Launcher

When primary managed game evidence ends:

```text
GAME_EXIT_DETECTED
→ RETURNING_TO_LAUNCHER
→ LAUNCHER
```

Committed mode remains GAME.

---

# 10. Game Profile / optimization

## VA-PROFILE-001 — Deterministic Desktop/TV selection

With both known Desktop and Living Room profiles, verify explicit/context preference order produces deterministic selection and ambiguous context requests user choice rather than opaque scoring.

---

## VA-PROFILE-002 — Field-level user lock preserved

Lock one supported setting, request Optimize.

Expected optimizer changes only unlocked fields subject to constraints; if target becomes impossible, report target unmet with user locks rather than silently overriding lock.

---

## VA-PROFILE-003 — Config write conflict

Change game config between adapter read and intended write.

Expected source digest conflict; fresh external/user change is not overwritten.

---

## VA-PROFILE-004 — External game setting drift

Change supported setting inside game.

Next run preserves external change for immediate launch and surfaces reconciliation; it does not silently create permanent user lock.

---

# 11. Launcher / Shared Apps

## VA-LAUNCHER-001 — Launcher readiness gates GAME commit

Prevent Launcher from reaching required precommit readiness.

Expected: GAME not committed solely because process exists.

---

## VA-LAUNCHER-002 — Hidden Launcher does not process gameplay navigation

During `GAME_RUNNING`, send ordinary controller navigation/actions.

Expected: hidden Launcher does not move focus/activate controls.

---

## VA-LAUNCHER-003 — Route/focus restoration after game

Launch from Game Details, exit game, confirm Launcher restores valid semantic route/focus bookmark.

---

## VA-SHARED-001 — Maximum 3 active assignments

Attempt a fourth active Shared App assignment.

Expected: Manage/Replace flow; no hidden fourth active assignment.

---

## VA-SHARED-002 — Overlay unavailable degrades locally

Use context where ordinary-window overlay cannot be guaranteed.

Expected: explicit `OVERLAY_UNAVAILABLE`/equivalent with alternative presentation; Game Mode/game session remain usable.

---

## VA-SHARED-003 — Window placement bounded retry

App repeatedly repositions its own window.

Expected: SplitOS performs bounded/debounced retries then reports degraded/drift; no infinite placement fight.

---

# 12. Update / recovery

## VA-UPDATE-001 — Update cannot start without verified previous-release capsule

Force capsule creation/verification failure.

Expected: target may remain staged but activation blocked.

---

## VA-UPDATE-002 — SplitOS feed cannot replace Windows patch authority

Verify wrapper feed contains/authorizes SplitOS-owned payload only; Microsoft Windows patch payload remains Microsoft servicing responsibility.

---

## VA-UPDATE-003 — Reboot/resume before commit

Interrupt update after partial activation but before durable target commit.

Expected source release remains canonical; resume/rollback/recovery converges safely.

---

## VA-UPDATE-004 — Target commit only after health verification

Target files/services starting is insufficient. Commit only after required Broker/Runtime/DB/compatibility/artifact verification.

---

## VA-RECOVERY-001 — Software rollback preserves current user data

Create/change Game Profile after N→N+1 update; then recover N+1→N.

Expected current user profile/preference data remains preserved per rollback compatibility contract.

---

## VA-RECOVERY-002 — Unauthorized old signed release rejected

Attempt normal downgrade to authentic old release without current authorized recovery edge.

Expected: denied.

---

## VA-RECOVERY-003 — Authorized exact recovery edge succeeds

Use valid `RecoveryAuthorization(N+1→N)` plus valid local capsule.

Expected recovery to exact target, followed by verification; no generic arbitrary downgrade.

---

# 13. Observability / privacy

## VA-OBS-001 — Correlated mode timeline

Execute Work→Game and build timeline using operation/transaction/correlation events.

Expected timeline explains request→apply→verify→commit without treating log as state replay.

---

## VA-OBS-002 — Diagnostic bundle excludes raw DB by default

Create standard incident bundle.

Expected no unrestricted raw `machine.db`/`user.db`, no whole game library/process history, and optional dumps/traces excluded unless selected.

---

## VA-OBS-003 — Secret redaction fail-closed

Inject forbidden secret fixture into a candidate diagnostic field/path.

Expected exporter redacts or blocks; never falls back to raw export.

---

## VA-OBS-004 — No implicit cloud telemetry

Run default v1 product without explicit support upload.

Expected local observability does not transmit hardware/game/log/dump diagnostics to SplitOS backend merely because they were collected locally.

---

# 14. Result

This catalog is the minimum semantic acceptance baseline. Client/game/hardware/release-specific suites extend it through the frozen `ReleaseAcceptanceProfile` and compatibility matrix.
