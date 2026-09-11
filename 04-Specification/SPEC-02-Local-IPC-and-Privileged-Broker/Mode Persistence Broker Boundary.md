# Mode persistence Broker boundary — Slice 03

## Purpose

RuntimeHost owns mode-transition ordering. Broker owns all SQLite access to machine-canonical mode state. RuntimeHost must not construct a machine repository or receive a writable machine database path.

The source-compatible durable models and repository interfaces now live in `SplitOS.Contracts/ModePersistence`. Their existing `SplitOS.Persistence.Machine` namespace is retained, but they do not depend on SQLite. `SplitOS.RuntimeHost` no longer references the `SplitOS.Persistence.Machine` implementation assembly.

## Wire capability

`Machine.Mode.Persistence@1` is the bounded Slice-03 specialization of the machine-state persistence extension. It uses the existing versioned Named Pipe envelope and frame-size limit. Only explicitly dispatched request types are accepted; no reflection-based method invocation is used.

| Group | Allowed operations |
| --- | --- |
| Machine state | Read full canonical OperationalMode; assert store readiness |
| MODE lease | Read, acquire, renew, release; assert readiness |
| Transition | Read by ID, list incomplete, create, advance; assert readiness |
| Resolved policy | Read and bind immutable policy identity; assert readiness |
| Action plan | Read and persist a complete immutable managed-service plan; assert readiness |
| Action journal | Read, begin apply, record apply result, begin verify, record verify result; assert readiness |
| Atomic commit | Commit verified transition and canonical OperationalMode in the existing SQLite transaction |

The exact version-1 message names and payload fields are defined by `ModePersistenceProtocol.cs`. Success responses use the request message name plus `Result` and a typed `value` wrapper. A missing record is represented by an explicit null value; a missing required success value is a protocol error.

`Initialize` requests only check readiness. In particular, `MachineStateInitializeRequest` reads the existing canonical state: it cannot bootstrap a missing database or run a schema migration. Broker startup remains the initialization owner.

## Validation and authority

- Broker rechecks the OS-derived caller image and current physical-console session for every message, including after a successful handshake. Manager and GameLauncher are not allowed Broker callers.
- Missing required constructor fields, null non-nullable fields, unknown fields, duplicate top-level fields, invalid envelopes, and unsupported message types are rejected.
- Payload operation IDs must match envelope operation IDs. Acquire/create correlation IDs must also match their envelopes. Runtime proxy continuation requests recover the original correlation from durable transition/lease state. A request whose owner no longer exists uses its operation ID as a stable fallback correlation; this does not grant mutation authority.
- MODE persistence cannot acquire UPDATE/RECOVERY leases or renew/release their active leases.
- Existing repository checks retain ownership, expected revision, lease lifetime, fencing, lifecycle, replay, policy identity and atomic commit invariants.
- Action-plan payloads accept only the existing managed-service action schema with a matching desired-state digest. SQL, database/file paths, arbitrary service names and untyped executable action payloads are not supported.
- `runtimeAccessPermitsTarget` and transition decisions remain assertions of the authenticated RuntimeHost semantic owner, never of the UI. This increment does not implement a new entitlement authority or enable a UI mode command.

Broker returns validation/availability errors through the existing error envelope. Transport loss is never converted into a successful local database write. Runtime recovery must inspect durable state before deciding whether an interrupted command needs replay or reconciliation; the proxy does not automatically retry mutations.

## Compatibility

### Recovery persistence extension

The same allowlisted capability now exposes `IModeTransitionRollbackStore` and
`IModeTransitionReconciliationStore`. Their shared DTOs live in Contracts; RuntimeHost
still has no machine SQLite implementation dependency. Broker owns their store instances.

Recovery requests support inspection, same-session lease takeover, terminal lease cleanup,
reverse-order rollback candidate selection, and rollback journal advancement. Takeover
checks the envelope operation/correlation against the durable transition. The repositories
retain atomic session, revision, lifecycle and fencing checks. Cleanup can release only an
exact coherent terminal MODE owner's lease; UPDATE and RECOVERY ownership is preserved.

`RuntimeModeRecoveryCoordinator.PrepareAsync` can cancel an expired/released same-session
operation with no possible mutation evidence, then clean its terminal lease. An unexpired
owner causes a wait. A foreign logon requires explicit BASE convergence. Unknown apply or
rollback outcomes and a committed managed target remain actual-state verification work;
none is converted into a successful recovery by reading the journal alone.

Recovery cancellation from REQUESTED, INSPECTING or APPLYING is supported in addition to
the existing cancellation states. An atomic action-evidence check forbids cancellation
outside rollback if an action could already have mutated Windows. This covers a crash at
ACCEPTED or APPLY_STARTED without inventing completed inspection or rollback stages.

The coordinator is registered for composition, but is not yet called by RuntimeHost startup.
A stable authenticated logon identity, a recovery Windows observation/compensation executor,
and startup ordering with normal operations remain necessary. Rollback journal replay is
an observation of a persisted result, not authorization for another Windows mutation.

No SQLite schema change or migration is introduced. The executable and Contracts assemblies must be delivered together: moving public model types between assemblies preserves source names, not compatibility with an independently deployed old binary. Unsupported peers fail through existing IPC error handling.

## Verification boundary

### Managed service compensation

`Machine.Service.Policy.Rollback@1` is a separate mutation capability, not a persistence
operation. Its request contains only durable ownership identifiers; callers cannot supply
service names, target entries or restoration states. Broker validates typed immutable
pre-state and target membership, current rollback lifecycle and reverse ordering. It
revalidates the fence immediately before every service operation and verifies actual
read-back before reporting success. Only RUNNING/STOPPED restoration is supported;
transitional or unreadable state returns an unresolved result.

Runtime records rollback completion per action only after Broker verification. Transport
loss preserves the in-flight journal. No source/target canonical mode is changed by this
capability. Restoring all captured values is not itself proof of the entire source policy
or current entitlement. The current Runtime step executor therefore only handles failed
activation from NONE and leaves managed-source recovery and terminalization explicit.

The fresh-source-authority extension permits WORK/GAME compensation only when Runtime
confirms the current logon, canonical source identity and freshly evaluated managed-mode
access. Runtime rechecks authority after Broker verification before recording rollback
completion. Denial preserves pending reconciliation and requests BASE convergence; it
never changes the canonical mode to NONE as a substitute for convergence. Compositions
without a source authority retain the NONE-only restriction above. Whole-source policy
verification and transition terminalization remain separate requirements.

The service-plan completion extension provides read-only
`Machine.Service.Policy.VerifySource@1`. Its identity-only request binds to the current
ROLLING_BACK transition, lease, fence and expected transition revision. Broker retrieves
the canonical source's prior committed operation and checks its complete typed service
plan, policy binding and target coverage before observing all source services. It accepts
no caller-supplied target values. Unsupported action domains, missing source plans and
conflicting targets require reconciliation. Under the clarified BASE restoration policy,
initial bootstrap NONE may instead be verified from immutable pre-activation captures of
the compensated services; their digests, target membership and actual restoration are checked.

After successful verification and fresh source authority, Runtime can persist
ROLLBACK_VERIFY and FAILED_WITH_SAFE_FALLBACK and release the terminal lease. It does not
write a new canonical mode. Retrying from ROLLBACK_VERIFY observes the source again;
the durable stage alone is not reused as fresh actual-state evidence. Startup maintenance
now invokes same-logon compensation recovery before idle access-loss handling. It waits for
live owners, takes over expired ownership, renews between bounded compensation steps and
terminalizes only after source verification. Unknown forward outcomes, post-commit recovery,
fresh-logon convergence and access loss during incomplete managed operations remain pending.

`Machine.Mode.BasePolicy.Read@1` resolves the original service baseline for an owning
DEACTIVATE operation in RESOLVING/RESOLUTION_STARTED. It accepts only source revision and
control-session identity; envelope identities must match the current MODE owner. Broker
locates the completed activation that began managed ownership and validates original
snapshots plus coverage of current service targets. Runtime persists the resolved baseline
as an ordinary typed BASE action and runs apply/verify/atomic commit through existing paths.

RuntimeHost now periodically invokes access-loss deactivation for an idle, same-logon
managed source. It does not adopt another logon, clear an incomplete transition, or interpret
canonical NONE as verified BASE when mutation evidence remains. The watcher is separate
from account/settings read capabilities. General crash recovery and broader non-service
policy domains are still outstanding.

`RuntimeModeOrchestratorTests` runs both direct-repository and Broker-proxy variants. The proxy variants use actual Named Pipe framing, Broker dispatch, and a temporary SQLite database. They include the complete mode cycle, FREE rejection, nullable reads, replay/stale fencing, malformed messages, response identity checks, and verification failure before canonical commit.

One scenario also routes service snapshot/apply/verify through the actual Broker executors and durable evidence validators, replacing only the Windows service adapter with an in-memory implementation. This proves composition without stopping any host Windows service.

These component/transport tests do not certify a LocalSystem installation, separate user/process ACL isolation, real SCM mutation, or crash/reboot recovery in a clean VM. Installed Windows acceptance and executable rollback/reconciliation remain required before declaring the whole Slice-03 complete.
