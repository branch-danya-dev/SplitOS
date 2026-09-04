# SPEC-07 — Shared Adapter Contract

## 1. Purpose

This document defines the concrete semantic DTOs and operation contracts that every Game Client adapter implements.

The contract is intentionally mechanism-neutral:

```text
Steam URI / VDF
Epic protocol / manifest
Windows Package / AUMID
Battle.net local mechanism
```

all normalize into the same SplitOS-facing surface.

---

## 2. Adapter registration

Each adapter is registered by immutable `clientType`:

```text
STEAM
EPIC
MICROSOFT_GAMING
BATTLENET
```

Conceptual registration:

```text
GameClientAdapterDescriptor
{
  clientType
  adapterVersion
  supportedExternalIdKinds[]
  capabilities[]
  compatibilityPolicyId
}
```

Two adapters MUST NOT claim the same `clientType` simultaneously in one release.

---

## 3. Capability descriptor

```text
AdapterCapability
{
  capabilityId
  mechanismStatus
  mechanismId
  minimumWindowsBuild?
  minimumClientVersion?
  maximumValidatedClientVersion?
  requiresFreshLibraryEvidence
  notesCode?
}
```

`mechanismStatus` is runtime/reportable data and may change after compatibility evaluation.

---

## 4. Discovery operation

```text
DiscoverClient(
  sessionId,
  freshnessRequirement
)
→ ClientDiscoveryEvidence
```

The adapter MUST observe the current Windows user/session context.

Discovery does not implicitly start the client.

---

## 5. Discovery evidence

```text
ClientDiscoveryEvidence
{
  clientType
  availabilityState
  registeredProtocolHandlers[]
  executablePathEvidence?
  packageIdentityEvidence?
  clientVersionEvidence?
  observedAtUtc
  generation
  mechanismStatus
}
```

Rules:

- executable paths are normalized before use;
- protocol registration means handler presence, not authenticated account state;
- client process running is separate from client installed/available;
- a missing process does not mean client unavailable.

---

## 6. Library refresh request

```text
RefreshLibraryRequest
{
  refreshId
  clientType
  reason
  freshnessRequirement
  previousGeneration?
  deadlineUtc
}
```

Freshness requirements:

```text
ALLOW_CACHED
FRESH_IF_AVAILABLE
FRESH_REQUIRED
```

`FRESH_REQUIRED` launch preparation fails if only stale last-known evidence is available.

---

## 7. Refresh result

```text
LibraryRefreshResult
{
  refreshId
  clientType
  resultCode
  generation
  observedAtUtc
  clientVersionObserved?
  records[]
  parserStatus?
  partialEvidence
}
```

Result codes:

```text
REFRESHED
NO_CHANGE
CLIENT_UNAVAILABLE
SOURCE_UNAVAILABLE
SOURCE_SCHEMA_UNKNOWN
PARSE_FAILED
TIMEOUT
PARTIAL
```

`PARTIAL` MUST identify which evidence classes are missing.

---

## 8. Projection record

```text
AdapterGameProjection
{
  externalGameIdentity
  displayNameEvidence?
  installation
  launchIdentity?
  executableCandidates[]
  sourceProvenance
}
```

Display name is metadata evidence only and may differ by locale/client.

---

## 9. Installation evidence

```text
AdapterInstallationEvidence
{
  state
  validatedInstallRoot?
  observedAtUtc
  expiresAtUtc?
  mechanismStatus
  sourceRecordIdentity?
  sourceRecordRevisionHint?
  confidence
}
```

Confidence values:

```text
HIGH
MEDIUM
LOW
```

`LOW` cannot satisfy a mandatory fresh launch precondition without an explicitly allowed fallback.

---

## 10. Launch identity

A launch identity is opaque outside the adapter but serializable into the projection cache.

```text
AdapterLaunchIdentity
{
  kind
  schemaVersion
  payload
  observedAtUtc
  mechanismStatus
}
```

Examples:

```text
STEAM_APP_ID
EPIC_SANDBOX_CATALOG_ARTIFACT
EPIC_INSTALL_PATH
MICROSOFT_AUMID
BATTLENET_PRODUCT_CODE_EXPERIMENTAL
```

Payload is validated against adapter-owned schema before use.

---

## 11. PrepareLaunch

```text
PrepareLaunch(
  GameClientLaunchRequest,
  currentProjection,
  currentClientEvidence
)
→ PreparedClientLaunch
```

Prepared result:

```text
PreparedClientLaunch
{
  launchOperationId
  clientType
  normalizedExternalGameIdentity
  resolvedLaunchIdentity
  requiredObservationRules
  handoffMechanism
  handoffMechanismStatus
  projectionGeneration
  expiresAtUtc
}
```

This object is immutable for the launch attempt.

---

## 12. Preparation validation

PrepareLaunch MUST reject:

- client mismatch;
- unknown external ID kind;
- stale mandatory projection;
- unvalidated path identity;
- unsupported client version where required;
- malformed URI identity;
- launch identity from a different game projection;
- user-supplied arbitrary command line.

---

## 13. SubmitLaunch

```text
SubmitLaunch(PreparedClientLaunch)
→ ClientLaunchHandoffResult
```

Normalized result:

```text
ClientLaunchHandoffResult
{
  launchOperationId
  resultCode
  submittedAtUtc
  clientProcessEvidence?
  returnedProcessIdentity?
  diagnosticsCode?
}
```

The adapter MUST NOT return `GAME_RUNNING` from `SubmitLaunch`.

---

## 14. Handoff result semantics

### `HANDOFF_ACCEPTED`

The platform/OS accepted the protocol/application activation sufficiently to begin observing the launch.

### `CLIENT_INTERACTION_REQUIRED`

The adapter can prove or strongly observe that external UX is required, but cannot safely classify the exact reason.

### `AUTH_REQUIRED`

Only valid when the adapter has a supported/validated signal for authentication requirement.

### `UPDATE_REQUIRED`

Only valid when the adapter has a supported/validated signal that an update is required.

Otherwise use `CLIENT_INTERACTION_REQUIRED` or timeout.

---

## 15. Observation rules

`PreparedClientLaunch.requiredObservationRules` may include:

```text
expectedExecutableNames[]
validatedInstallRoot?
expectedAumid?
expectedPackageFamilyName?
knownBootstrapExecutables[]
allowedProcessReplacementPatterns[]
minimumRunningEvidence
startStabilityWindowMs
exitGraceWindowMs
```

These values are adapter/per-game evidence, not global process-control permissions.

---

## 16. Observation API

```text
ObserveLaunch(context, windowsEvidence)
→ GameClientObservationResult
```

Result:

```text
GameClientObservationResult
{
  classification
  evidenceLevel
  correlatedProcesses[]
  replacementDetected
  observedAtUtc
  diagnosticsCode?
}
```

Classifications:

```text
NO_MATCH
CANDIDATE
STARTING_CONFIRMED
RUNNING_CONFIRMED
AMBIGUOUS
CORRELATION_LOST
```

---

## 17. Correlated process record

```text
CorrelatedProcessEvidence
{
  pid
  creationTimeUtc
  normalizedImagePath?
  sessionId
  executableRole
  evidenceLevel
  firstObservedUtc
  lastObservedUtc
}
```

Executable roles:

```text
BOOTSTRAP
PUBLISHER_LAUNCHER
GAME_PRIMARY
GAME_SECONDARY
UNKNOWN_CANDIDATE
```

---

## 18. Running confirmation

`RUNNING_CONFIRMED` requires one adapter-defined supported proof set.

Example generic proof set:

```text
expected image under validated install root
+
same interactive user session
+
created after handoff / wasn't in baseline
+
stable for minimum observation interval
```

Alternative proof for packaged apps:

```text
expected AUMID/package activation
+
matching process/application identity
+
current session
```

The adapter MUST record which proof set produced confirmation.

---

## 19. Exit observation

```text
ObserveExit(
  sessionCorrelation,
  currentWindowsEvidence
)
→ ExitObservationResult
```

Result:

```text
STILL_RUNNING
EXIT_CANDIDATE
EXIT_CONFIRMED
REPLACEMENT_PROCESS_FOUND
CORRELATION_LOST
```

The session owner, not adapter, performs Game Session state transition.

---

## 20. Correlation replacement

Some games replace bootstrap processes.

If a correlated process exits and a permitted replacement appears within the configured correlation window:

```text
old process exits
+
valid replacement appears
→ GAME_PROCESS_REPLACED
→ session remains active
```

A random foreground process is not a permitted replacement.

---

## 21. Client process vs game process

The following must never be conflated:

```text
Steam.exe running
EpicGamesLauncher.exe running
XboxPcApp.exe running
Battle.net.exe running
```

with:

```text
game running
```

Client process evidence may help diagnose handoff but never establishes Game Session running state.

---

## 22. Client start policy

Adapters MAY cause the client to start only through the documented/validated game launch mechanism itself.

There is no generic requirement:

```text
ensure client process running first
```

because registered protocol/application activation may start it automatically.

If an adapter explicitly needs client startup, that is a client-specific capability and must be versioned/tested.

---

## 23. File watcher contract

Version-sensitive local metadata adapters MAY register watchers on their known metadata roots.

Watcher event:

```text
path changed
→ sourceGeneration++
→ related projections STALE
→ schedule refresh
```

Rules:

- watcher events are debounced;
- watcher overflow forces full refresh;
- rename/write event does not itself mean install/uninstall;
- files are opened with sharing modes compatible with the client where possible;
- transient parse failure during client write is retried only within a bounded window.

---

## 24. Compatibility gating

Before using a `VERSION_SENSITIVE` parser/launch mechanism:

```text
observed client version
+
known schema signature
+
adapter compatibility data
→ capability decision
```

Unknown version may permit public protocol launch while disabling local library parsing.

---

## 25. Security limits

Adapter operation accepts no:

```text
raw executable to launch
raw shell command
raw PowerShell
arbitrary URI supplied by UI
arbitrary registry path
arbitrary manifest path
```

UI/Game Profile references semantic game/client IDs only.

---

## 26. Cancellation

Cancellation before handoff:

```text
cancel Prepare/Submit
→ no launch attempt
```

Cancellation after `HANDOFF_ACCEPTED` cannot revoke an external client launch already dispatched.

Instead:

```text
stop observing / mark request cancelled
```

unless a separate supported client cancellation mechanism exists.

SplitOS MUST NOT force-kill the game/client merely to simulate cancellation.

---

## 27. Idempotency

Managed launch orchestration supplies one `launchOperationId`.

Adapter MUST detect duplicate SubmitLaunch calls in-process for the same immutable prepared launch and avoid accidental repeated protocol activation where practical.

Cross-process durable launch idempotency is not guaranteed by external clients; recovery therefore relies on process/session evidence before resubmitting after Runtime Host crash.

---

## 28. Runtime Host crash during launch

After restart in the same Windows logon:

```text
persisted/known Game Session launch attempt
+
current process evidence
+
client projection
→ reconcile before any relaunch
```

If strong evidence says game already started:

```text
attach/reconcile existing session
```

not:

```text
blindly submit launch again
```

Exact Game Session persistence remains owned by Game Launch/Session specification state.

---

## 29. Result contract to Game Library

Game Library receives only normalized projections:

```text
client type
external identity
installation evidence
launch identity availability
freshness
support status
```

It does not receive raw manifest objects or client databases.

---

## 30. Result contract to Game Launch

Game Launch receives:

```text
PreparedClientLaunch
ClientLaunchHandoffResult
observation policy
```

It does not receive passwords, internal client auth data or unrestricted shell commands.

---

## 31. Result contract to Game Session

Game Session receives normalized evidence:

```text
STARTING_CONFIRMED
RUNNING_CONFIRMED
EXIT_CONFIRMED
CORRELATION_LOST
```

The adapter never directly sets Game Session enum.

---

## 32. Acceptance criteria

A conforming shared adapter layer demonstrates:

- capability status is per capability;
- stale local evidence stays stale;
- handoff and running are separate;
- weak evidence cannot establish running;
- client update can disable parser but preserve public launch;
- raw metadata never escapes into privileged/system command surfaces;
- Runtime restart reconciles before relaunch;
- each adapter can be independently disabled without changing Game Library/Game Launch semantic contracts.
