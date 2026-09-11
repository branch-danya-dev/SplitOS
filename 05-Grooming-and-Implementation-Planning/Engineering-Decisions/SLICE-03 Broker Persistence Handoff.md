# Slice-03 Broker persistence handoff

## Delivered increment

The mode orchestrator and service apply/verify coordinators depend on transport-neutral repository interfaces. The production RuntimeHost dependency registrations bind those interfaces to `NamedPipeModePersistenceClient`; Broker registers the concrete SQLite repositories and `BrokerModePersistenceHandler`.

The machine implementation project reference has been removed from RuntimeHost. A regression assertion checks the compiled assembly dependency, making accidental reintroduction of direct machine-store access visible.

Owner: SPEC-02 machine-state persistence extension and SPEC-05 mode runtime. See [Mode Persistence Broker Boundary](../../04-Specification/SPEC-02-Local-IPC-and-Privileged-Broker/Mode%20Persistence%20Broker%20Boundary.md).

## Reproduce the focused checks

Use the SDK pinned by `global.json` (10.0.110), then run from the repository root:

```powershell
./eng/verify-slice03-mode-persistence.ps1
```

This workspace also has an isolated SDK under `work/dotnet`:

```powershell
./eng/verify-slice03-mode-persistence.ps1 -DotNet ./work/dotnet/dotnet.exe
```

The harness creates temporary databases and Named Pipes. It does not install a Windows Service or mutate the host service catalog. SCM calls in the composed Broker scenario are replaced by an in-memory service adapter.

## Local verification — 2026-09-10

`SplitOS.sln` builds in Release/x64 with SDK 10.0.110, with zero errors. Existing analyzer/package warnings remain.

| Suite | Passed | Skipped |
| --- | ---: | ---: |
| RuntimeHost (including 17 mode-orchestrator/boundary cases) | 254 | 0 |
| Contracts | 10 | 0 |
| Broker | 39 | 1 |
| IPC integration | 3 | 0 |
| Persistence | 88 | 0 |
| Total distinct cases | 394 | 1 |

Seven protected-secret tests initially failed because the sandbox refused Windows ACL changes on test fixtures. The protected-secret subset was rerun outside the sandbox: all eight cases in that subset passed. The totals above count overlapping successful cases once. No application code was changed to relax ACL protection.

The skipped Broker case requires `SPLITOS_CI_MANAGED_SERVICE_NAME` and a dedicated Windows service fixture. No such service was installed during this run. Test result files for the separate suites and the protected-secret rerun are under `work/test-results`; build logs are under `work`.

## Remaining Slice-03 work

1. Connect an authenticated semantic mode command and a release-owned target preparation provider to the running RuntimeHost. Continue deriving access/session/activation identities inside RuntimeHost rather than accepting them from the UI.
2. Complete actual-state rollback and startup reconciliation over the Broker boundary. Recovery persistence and a preparation coordinator are now implemented (see below). The current mode error path still records rollback-required state; Windows compensation and the startup caller remain outstanding.
3. Prove installed topology in a clean Windows VM: LocalSystem Broker, unelevated RuntimeHost, protected machine state, one approved service target, read-back, and crash checkpoints before/after commit.
4. Complete durable user-decision blockers and long-operation lease renewal before expanding to broad Windows policies.

The current increment removes the direct-SQLite integration obstacle. It does not mark all of Slice-03, production auth, or the Manager mode UX complete.

## Recovery preparation increment — 2026-09-10

Added Broker-backed rollback and reconciliation store interfaces, shared models, strict typed
requests, proxy methods and production dependency registrations. No schema migration is needed.

`RuntimeModeRecoveryCoordinator.PrepareAsync` waits for a live owner, re-fences an expired
same-logon transition, cancels only a transition without possible mutation evidence, and
cleans up a proven terminal lease. A lost cancellation response is recovered by a fresh
client inspecting durable state. Fresh-logon ownership is not adopted; committed target
truth is preserved. Unknown Windows outcomes remain explicit pending reconciliation.

Fixed cancellation at early crash checkpoints: REQUESTED and INSPECTING may now cancel;
APPLYING may cancel only before action mutation evidence exists. Cancellation checks the
action journal inside the same SQLite transaction as the transition update. Tests verify
that PLANNED can cancel while APPLYING and APPLIED actions block cancellation.

This is recovery preparation, not an installed automatic Windows rollback implementation.
The coordinator is registered but startup does not invoke it yet. Next: implement a typed
Broker recovery executor that derives compensation from captured pre-state, validates the
current fence immediately before each service operation, reads actual state after ambiguous
responses, and verifies source/target before terminalization. Then connect startup with a
stable Windows logon identity and serialization against ordinary mode operations.

Validation: Release/x64 solution build passed with SDK 10.0.110, zero errors (151 warnings).
The updated focused suite has 25 passing cases. Full regression totals:

| Suite | Passed | Skipped |
| --- | ---: | ---: |
| RuntimeHost | 262 | 0 |
| Contracts | 10 | 0 |
| Broker | 39 | 1 |
| IPC integration | 3 | 0 |
| Persistence | 91 | 0 |
| Total distinct cases | 405 | 1 |

The sandbox run excluded two `ProtectedSecret` cases but still encountered five ACL-denied
tests under other protected-store class names. All eight `Protected` cases passed on a
targeted rerun outside the sandbox; the table counts their overlap only once. No ACL
protection was relaxed. Logs and TRX reports use the `recovery-`/`Recovery-` prefix under
`work`. The skipped Broker test still needs a dedicated real Windows service fixture.

## Executable service compensation increment — 2026-09-11

Added `Machine.Service.Policy.Rollback@1` with an identity-only request: transition,
action, lease, fence, session and action revision. Broker derives every service target
and restoration value from canonical pre-state, checks its digest and exact target
membership against the immutable desired action, and resolves only release-owned targets.

`ModeMutationFenceStore.ValidateRollbackAsync` independently validates current ownership,
ROLLING_BACK/ROLLBACK_STARTED, pre-commit state, action revision and reverse ordering.
The forward mutation validator still accepts only APPLYING. Journal replay is never used
as authorization to invoke the Windows adapter. Broker checks the rollback fence again
after observation and immediately before each service mutation, and after final read-back.

`ManagedServiceActionRollbackCoordinator.ExecuteNextAsync` compensates one action and
records ROLLED_BACK only after actual-state verification. Unknown observations, exceptions
and lost responses leave ROLLING_BACK durable. Repeating an interrupted step first reads
Windows state and avoids reapplying a restoration already satisfied. It does not terminalize
the transition or release its lease; source-policy verification is still required.

The Runtime coordinator currently supports failed activation from NONE. A WORK/GAME source
returns MODE_ROLLBACK_SOURCE_AUTHORITY_REQUIRED until fresh source authorization and BASE
fallback are integrated. Broker remains a technical executor; entitlement decisions belong
to Runtime. Neither this coordinator nor recovery preparation is automatically invoked by
startup or the ordinary orchestrator error path yet.

Verification: solution build Release/x64, SDK 10.0.110, zero errors (153 warnings).
RuntimeHost: 270 passed; Contracts: 10; Broker: 39 passed/1 skipped; IPC: 3;
Persistence: 92 (84 ordinary tests plus 8 protected-store tests outside the sandbox).
Total: 414 passed, 1 skipped. Reports use `Rollback-` under `work/test-results`.
Nine new cases cover rollback fencing, actual forward/rollback Broker composition, lost
reply, adapter exception after mutation, false success claims, lease expiry between query
and mutation, stale fence after takeover, forbidden payload targets, committed mode and
fresh managed-source authority. The transport tests use an in-memory Windows adapter;
no host service was stopped, and installed LocalSystem/SCM acceptance remains outstanding.

Next: source-policy verification and authorization, BASE convergence when source access
is unavailable, followed by startup/error-path integration and bounded lease renewal.

## Fresh source authority increment — 2026-09-11

Added `RuntimeModeSourceAuthority`, wired to the canonical machine proxy, current Windows
control-session identity and the existing account-association/entitlement evaluator.
It checks source mode/revision, same-logon ownership, managed source policy identity and
activation epoch. WORK/GAME require a fresh access evaluation; NONE requires no managed
entitlement. Source/session context is read again after asynchronous access evaluation.

The rollback coordinator now checks this authority before Broker compensation and after
verified read-back, before persisting ROLLED_BACK. Revoked access returns
MODE_SOURCE_AUTHORITY_BASE_CONVERGENCE_REQUIRED and preserves the unresolved journal.
Production DI supplies this authority. A legacy composition without it still cannot
compensate a managed source. Therefore the earlier NONE-only limitation is lifted only
when a source authority is supplied and confirms the current context.

`WindowsControlSessionIdentity` derives a versioned key from the token authentication LUID,
LSA logon time, user SID and console session. It accepts only supported interactive logons
in the active console and validates the token/logon user. The key does not contain process
identity and is not automatically assigned to legacy transition keys. A previous unbound
session key fails the same-logon check and must follow explicit reconciliation.
Native definitions follow Microsoft documentation for
[TOKEN_STATISTICS](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-token_statistics)
and [SECURITY_LOGON_SESSION_DATA](https://learn.microsoft.com/en-us/windows/win32/api/ntsecapi/ns-ntsecapi-security_logon_session_data).

Validation: Release/x64 solution build has zero errors. All 281 RuntimeHost tests pass,
including 11 new source-authority/key/rollback cases. Other unchanged suites were not
rerun for this increment. A native smoke check refused the sandbox's non-console logon
as designed; the same read-only check outside the sandbox successfully read active-console
logon data twice with identical keys. No SID or logon key was written to the smoke logs.
Reports: `work/test-results/SourceAuthority-RuntimeHost.trx`,
`work/source-authority-solution-build.log`, `work/source-authority-native-user.log`.

Still outstanding: source-policy actual-state verification as a whole, explicit BASE
convergence, terminalization and automatic startup/error-path invocation. Fresh authority
permits compensation; it does not claim that restoring per-action pre-state proves the
entire source policy. The repository has a policy model but no production release-owned
target preparation provider wired to RuntimeHost yet; that dependency must be completed
before declaring verified source convergence.

## Source service-plan verification and terminalization — 2026-09-11

Added read-only `Machine.Service.Policy.VerifySource@1`. Broker resolves the canonical
source operation to its completed transition and immutable action plan, checks source
mode/revision and policy binding, validates every typed service action and digest, requires
coverage of the interrupted plan's service targets, and reads every source target through
the release catalog. Missing, conflicting or unsupported evidence cannot produce VERIFIED.
Ownership and lease expiry are rechecked around observation.

Reconciliation snapshots now include the complete canonical session, activation epoch and
policy identity tuple, which the earlier read omitted. Partial tuples are rejected.

`RuntimeModeRollbackCompletionCoordinator.CompleteAsync` checks fresh source authority,
requests source service-plan verification, checks authority again, advances ROLLBACK_VERIFY,
rechecks authority after that write, records FAILED_WITH_SAFE_FALLBACK and cleans the
terminal lease. Canonical mode/revision stay unchanged. A lost stage reply causes fresh
verification on retry; a lost terminal reply is handled by terminal-lease cleanup.
This completion method handles technical failure, not user cancellation.

Restoring captured pre-state can still leave a source-policy mismatch; that remains
nonterminal until actual state matches the committed plan. Verification itself does not
repair drift. A previously committed BASE plan can verify NONE without premium access;
bootstrap NONE without such a plan returns MODE_SOURCE_POLICY_UNAVAILABLE.

Scope is the supported service plan. Display/audio/input and other action types are
rejected. Startup and the normal forward-error path do not invoke these coordinators yet.
Remaining: a release-owned initial BASE policy, explicit convergence after access loss,
automatic recovery ordering, bounded retries/lease renewal and installed VM proof.

Validation: Release/x64 solution build, zero errors (157 warnings). RuntimeHost 289 passed,
Contracts 10, Broker 39 passed/1 skipped, IPC 3, ordinary Persistence 84. This run totals
425 passed and 1 skipped. Eight unchanged protected-store/ACL tests were not rerun.
Eight new transport cases cover mismatch, missing initial BASE, known BASE, incomplete
rollback, lease expiry, entitlement loss and lost stage/terminal replies. Reports use
`SourceVerification-` under `work/test-results`; Windows service adapters are simulated.

## Ordinary Windows BASE and access-loss convergence — 2026-09-11

Product owner clarified that BASE must retain ordinary Windows functionality. Losing mode
access removes SplitOS mode changes, not native Windows features; profile, renewal and
read-only WORK/GAME settings are not premium-gated. This is recorded in SPEC-04 and SPEC-05.
For services, BASE restores pre-activation user state rather than forcing RUNNING/STOPPED.

Implemented `InitialBaseServicePolicy`: first-activation rollback can now verify native
BASE against immutable captured state of compensated services. This supersedes the earlier
initial-NONE-unavailable limitation. Original snapshots are checked for typed targets,
digest integrity, supported observations and completed compensation. For repeated targets,
the earliest pre-state wins because later snapshots may already contain SplitOS changes.

`Machine.Mode.BasePolicy.Read@1` resolves the baseline from the completed activation that
began current managed ownership. It validates current deactivation ownership and source
revision, original capture and current target coverage. `BaselineModeTargetPreparationProvider`
persists this restoration as an ordinary service BASE policy. It supports DEACTIVATE only;
it does not provide managed WORK/GAME target definitions or claim other policy domains.

`RuntimeModeAccessLossCoordinator` derives a DEACTIVATE command from fresh access and the
current same-logon canonical source. It uses normal apply, actual verification and atomic
commit, never a direct NONE write. Incomplete transitions, changed logon/context and failed
verification remain reconciliation work. `RuntimeModeAccessLossService` is registered as a
RuntimeHost background service and checks every 30 seconds, logging changes rather than
repeating identical status. Expected native/storage/availability failures do not terminate
the host or disable account UI. The existing Manager account controls depend on association
state, not premium-mode access; full profile/renewal/settings screens are separate UI work.

Verification: Release/x64 solution build, zero errors (162 warnings). RuntimeHost 301 passed,
Contracts 10, Broker 39 passed/1 skipped, IPC 3, ordinary Persistence 84: 437 passed, 1 skipped
in this run. The eight unchanged protected-store tests were not rerun. Twelve new cases
cover idle access loss, retained access, unresolved NONE, failed verification, another logon,
both original service states, switches, a new activation baseline, initial failure and
resolver ownership. Real Broker dispatch/Named Pipes/SQLite were used with simulated SCM.
No host Windows service was changed. Reports use `BasePolicy-` under `work/test-results`.

Remaining: automatic crash recovery for an already-incomplete transition (including access
loss during one), startup ordering and lease renewal for that path, real installed Windows
acceptance, broader mode domains and managed-target/UI composition. The new background
worker handles idle managed access loss; it deliberately does not clear or adopt unfinished
operation evidence to start a second deactivation.

## Automatic compensation recovery — 2026-09-11

`RuntimeModeAutomaticRecoveryCoordinator` now runs at the beginning of every background
maintenance pass, including startup, before idle access-loss deactivation. The existing
worker serializes both phases. Live owners are left alone; expired same-logon operations
are re-fenced through Broker. Proven unmutated requests are cancelled and terminal leases
are cleaned by the existing preparation coordinator.

For settled forward evidence or an interrupted compensation, recovery checks fresh source
authority, renews its two-minute lease before each step, enters ROLLING_BACK when needed,
executes reverse compensation and verifies the entire supported source service plan before
terminalization. Each pass is bounded to 64 steps and stops at the first unresolved result.
A lost response retains durable evidence; after lease expiry the next pass observes actual
state under a fresh fence. Individual service calls remain fenced and fail if the lease
expires during observation; renewal between steps is not an unlimited in-call heartbeat.

Idle access checking resumes only after recovery permits it; its own incomplete-operation
guard remains active. An idle committed mode is not rolled back by this coordinator and
permission to check access is not a claim that committed target drift has been verified.

Remaining recovery cases are explicit: unknown forward APPLYING outcomes, post-commit
finalization, earlier-logon ownership and BASE convergence when access disappears during
an incomplete managed transition. In particular, revoked access never authorizes restoring
WORK/GAME, but the interrupted operation is not yet converted to a BASE recovery plan.
These cases retain their journal and return a pending recovery product code. Broader policy
domains, installed Windows acceptance and full Manager screens also remain outstanding.

Validation: Release/x64 solution build succeeded with zero errors (73 warnings in this
incremental build). Full RuntimeHost suite: 310 passed, none failed or skipped. Nine new
cases exercise live-owner waiting, unmutated cancellation, ordinary/interrupted rollback,
foreign logon, revoked access, failed read-back retry, expiry during observation, settled
compensation verification and idle canonical preservation. Broker dispatch, Named Pipes
and SQLite are real; Windows service observations/mutations use the test adapter. Other
unchanged test suites and real host services were not exercised in this increment.
Report: `work/test-results/AutomaticRecovery-RuntimeHost.Tests.trx`.
