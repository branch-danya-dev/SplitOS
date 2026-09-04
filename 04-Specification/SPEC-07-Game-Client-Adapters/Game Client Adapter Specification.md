# SPEC-07 — Game Client Adapter Specification

## 1. Purpose

This specification defines the v1 implementation contract between SplitOS Game Library / Game Launch domains and external desktop game clients.

It covers:

```text
Steam
Epic Games Launcher
Microsoft Gaming / Xbox / Microsoft Store-backed games
Battle.net
```

The specification preserves the A&D rule:

```text
external client owns platform/account/license/install facts
↓
SplitOS adapter observes / launches through supported mechanisms
↓
SplitOS owns only normalized projection + managed launch/session semantics
```

A client adapter is not the authority for ownership, entitlement, installation or authentication merely because it parsed a local file.

---

## 2. Scope

SPEC-07 defines:

- adapter module contract;
- capability/status model;
- client discovery;
- local library/install evidence;
- external game identity;
- launch handoff;
- client interaction/auth-required outcomes;
- process/game correlation;
- game-start/game-exit evidence;
- cache refresh/invalidation;
- client-version compatibility handling;
- trust boundaries for local metadata;
- v1 client support posture.

SPEC-07 does **not** define:

- Game Profile schema/optimization policy (`SPEC-08`);
- Game Launcher visual/controller UX (`SPEC-09`);
- account credentials for external platforms;
- direct anti-cheat/DRM integration;
- game binary patching/injection;
- payment/store purchasing;
- exact per-game graphics configuration.

---

## 3. Physical placement

Adapters run in the interactive Windows user context under `SplitOS.RuntimeHost.exe`.

```text
Game Library / Game Launch
        ↓
GameClientAdapterRegistry
        ↓
┌────────────┬────────────┬───────────────┬──────────────┐
│ Steam      │ Epic       │ Microsoft     │ Battle.net   │
│ Adapter    │ Adapter    │ Gaming Adapter│ Adapter      │
└────────────┴────────────┴───────────────┴──────────────┘
        ↓
read-only local evidence / registered protocols / Windows activation
```

Rules:

- adapters run unelevated;
- adapters MUST NOT call Privileged Broker for normal library discovery or game launch;
- adapters MUST NOT modify client databases/manifests;
- adapters MUST NOT store client passwords/tokens;
- adapters MUST NOT inject into client/game processes.

---

## 4. Stable semantic contract

All adapters expose the same logical surface even when mechanisms differ.

Conceptual interface:

```text
GetCapabilities()
DiscoverClient()
RefreshLibrary(reason, freshnessRequirement)
ResolveInstallation(externalGameIdentity)
PrepareLaunch(gameClientBinding)
SubmitLaunch(launchRequest)
ObserveLaunch(launchObservationContext)
ObserveExit(gameSessionCorrelation)
Invalidate(reason)
GetCompatibilityStatus()
```

The exact programming-language interface is implementation detail, but the semantics are normative.

---

## 5. Capability set

Initial capability identifiers:

```text
CLIENT_DISCOVERY
CLIENT_VERSION_EVIDENCE
LIBRARY_DISCOVERY
INSTALLATION_EVIDENCE
LAUNCH_IDENTITY_RESOLUTION
LAUNCH_ELIGIBILITY_EVIDENCE
GAME_LAUNCH
CLIENT_INTERACTION_EVIDENCE
GAME_PROCESS_CORRELATION
GAME_EXIT_CORRELATION
ACCOUNT_CONTEXT_EVIDENCE
UPDATE_STATE_EVIDENCE
```

Each capability MUST publish a status independently.

One adapter being able to launch a game does not imply it can reliably enumerate the full library.

---

## 6. Mechanism status

Normative status enum:

```text
SUPPORTED_PUBLIC
SUPPORTED_OS_MECHANISM
SUPPORTED_VERSION_GATED
BEST_EFFORT_LOCAL_EVIDENCE
VERSION_SENSITIVE
USER_MEDIATED
OPEN
UNSUPPORTED
```

Meaning:

### `SUPPORTED_PUBLIC`

The platform/vendor documents the mechanism for the relevant use.

### `SUPPORTED_OS_MECHANISM`

Windows exposes a supported mechanism that can represent/activate the external application identity.

### `SUPPORTED_VERSION_GATED`

Supported only for validated Windows/client/package generations.

### `BEST_EFFORT_LOCAL_EVIDENCE`

Read-only local metadata is useful but is not a stable public platform contract.

### `VERSION_SENSITIVE`

Mechanism may break when client metadata/CLI/protocol behavior changes and therefore requires compatibility tests.

### `USER_MEDIATED`

SplitOS can direct the user to the external client's own UX but cannot perform the operation itself.

### `OPEN`

No acceptable implementation is committed yet.

### `UNSUPPORTED`

Explicitly excluded from supported behavior.

---

## 7. Adapter support level

Client-level support is separate from individual capability status.

```text
TARGET_SUPPORTED_V1
PARTIAL_SUPPORTED_V1
EXPERIMENTAL
NOT_SUPPORTED
```

A client MAY be called `TARGET_SUPPORTED_V1` only when release verification proves at minimum:

```text
CLIENT_DISCOVERY
+
INSTALLATION_EVIDENCE adequate for launcher UX
+
LAUNCH_IDENTITY_RESOLUTION
+
GAME_LAUNCH
+
GAME_PROCESS_CORRELATION
+
GAME_EXIT_CORRELATION
```

A public launch URI alone is insufficient for full-support labeling.

---

## 8. Current v1 support posture

Specification target:

| Client | v1 posture | Key note |
|---|---|---|
| Steam | `TARGET_SUPPORTED_V1` pending validation | documented `steam://` launch; library evidence remains version-sensitive local metadata |
| Epic Games Launcher | `TARGET_SUPPORTED_V1` pending validation | documented Epic protocol activation; local library/install metadata remains version-sensitive |
| Microsoft Gaming / Xbox | `PARTIAL_SUPPORTED_V1` | strong Windows package/AUMID path where title has registered app identity; universal Xbox library/license API not assumed |
| Battle.net | `EXPERIMENTAL` | no stable public universal library/game-launch contract accepted yet |

Release marketing/support MUST reflect the actually verified matrix, not this target intent.

---

## 9. Client discovery result

```text
ClientDiscoveryEvidence
{
  clientType
  availabilityState
  mechanism
  mechanismStatus
  executableIdentity?        // evidence, not authority by itself
  protocolRegistration?      // evidence
  packageIdentity?           // where applicable
  observedClientVersion?
  observedAtUtc
  snapshotGeneration
  diagnosticsCode?
}
```

Availability states:

```text
AVAILABLE_VERIFIED
NOT_FOUND_VERIFIED
AVAILABLE_UNVERIFIED_VERSION
STALE_LAST_KNOWN
UNKNOWN
```

No adapter may return `AVAILABLE_VERIFIED` solely because a stale cache row exists.

---

## 10. External game identity

SplitOS canonical `gameId` is independent from client identity.

Adapter identity concept:

```text
ExternalGameIdentity
{
  clientType
  externalIdKind
  externalId
  secondaryIds[]
}
```

Examples:

```text
Steam     → APP_ID / 1091500
Epic      → SANDBOX_CATALOG_ARTIFACT or validated install identity
Microsoft → PFN + AUMID / package-app registration
Battle.net→ product identifier only when mechanism is validated
```

Rules:

- install path MUST NOT be the canonical SplitOS game identity;
- executable path MUST NOT be the canonical SplitOS game identity;
- display title MUST NOT be the canonical SplitOS game identity;
- external identity may change/migrate only through explicit reconciliation.

---

## 11. Library/install evidence

Normalized record:

```text
GameInstallationEvidence
{
  clientType
  externalGameIdentity
  installState
  validatedInstallRoot?
  launchIdentity?
  evidenceSource
  evidenceSchemaVersion?
  observedAtUtc
  expiresAtUtc?
  freshness
  confidence
  mechanismStatus
  clientVersionObserved?
}
```

Install states:

```text
INSTALLED_VERIFIED_EVIDENCE
NOT_INSTALLED_VERIFIED_EVIDENCE
INSTALLING
UPDATE_REQUIRED_EVIDENCE
STALE_LAST_KNOWN
UNKNOWN
```

The word `VERIFIED_EVIDENCE` means SplitOS verified what the local evidence source currently says. It does not convert SplitOS into the platform's canonical install owner.

---

## 12. Projection cache

Adapter output is reconciled into SPEC-03 `projection.db`.

```text
external client/local registration
↓
adapter parse/normalize
↓
GameInstallationEvidence
↓
projection.db
```

Mandatory cache fields:

- source client;
- source mechanism;
- observation timestamp;
- freshness/expiry where applicable;
- client version where known;
- adapter version;
- mechanism status;
- normalized external identity.

If a parser fails after a client update:

```text
previous INSTALLED evidence
→ STALE_LAST_KNOWN
```

not:

```text
→ still INSTALLED_VERIFIED_EVIDENCE forever
```

---

## 13. Library refresh

Refresh reasons:

```text
RUNTIME_START
GAME_LAUNCH_REQUEST
CLIENT_STARTED
CLIENT_EXITED
CLIENT_VERSION_CHANGED
LOCAL_METADATA_CHANGED
USER_REFRESH
STALE_CACHE
POST_GAME_EXIT
```

Adapters SHOULD use event/file-system invalidation where reliable, but MUST support explicit refresh.

File-system events are invalidation hints only:

```text
file changed
→ projection stale
→ parse fresh source
```

They are not canonical install/uninstall events by themselves.

---

## 14. Launch request

Normalized request:

```text
GameClientLaunchRequest
{
  launchOperationId
  correlationId
  gameId
  clientType
  externalGameIdentity
  resolvedLaunchIdentity
  expectedInstallationEvidenceRevision
  userSessionId
  requestedAtUtc
  deadlineUtc
}
```

Rules:

- launch identity MUST be resolved from current evidence;
- stale install projection cannot satisfy a `FRESH_REQUIRED` launch requirement;
- game path/URI values obtained from external metadata are treated as untrusted input and validated before use;
- raw arbitrary command-line text from UI/Profile is forbidden.

---

## 15. Launch handoff result

Immediate adapter outcome:

```text
HANDOFF_ACCEPTED
HANDOFF_REJECTED
CLIENT_NOT_AVAILABLE
GAME_NOT_INSTALLED
CLIENT_INTERACTION_REQUIRED
CLIENT_VERSION_UNSUPPORTED
LAUNCH_IDENTITY_INVALID
STALE_EVIDENCE
MECHANISM_UNAVAILABLE
UNKNOWN_FAILURE
```

Critical invariant:

```text
HANDOFF_ACCEPTED
!= GAME_STARTING
!= GAME_RUNNING
```

For URI/shell activation, `HANDOFF_ACCEPTED` normally means only that Windows/client accepted dispatch sufficiently for observation to begin.

---

## 16. Authentication / client interaction

SplitOS does not collect external-client credentials.

If launch causes the client to show login/update/consent UI:

```text
external client owns UX
```

Adapter may report:

```text
CLIENT_INTERACTION_REQUIRED
AUTH_REQUIRED
UPDATE_REQUIRED
```

only when the mechanism provides evidence strong enough to distinguish them.

If the adapter cannot prove why launch is waiting, it MUST use the broader:

```text
CLIENT_INTERACTION_REQUIRED
```

and not invent `AUTH_REQUIRED`.

---

## 17. Launch observation context

Before handoff, Game Launch Orchestration creates a baseline observation context:

```text
LaunchObservationContext
{
  launchOperationId
  clientType
  externalGameIdentity
  expectedExecutableEvidence[]
  expectedInstallRoot?
  expectedPackageOrAumid?
  baselineProcessSnapshot
  baselineForegroundWindow?
  submittedAtUtc
  startingDeadlineUtc
  runningDeadlineUtc
  adapterSnapshotGeneration
}
```

This context is immutable for one launch attempt.

---

## 18. Process correlation principle

Launcher process ancestry is **not** sufficient as a universal rule.

External clients may:

- use helper services;
- launch through a broker;
- create bootstrap executables;
- spawn a publisher launcher first;
- restart the real game executable;
- use packaged-app activation.

Therefore correlation uses multiple evidence dimensions.

---

## 19. Correlation evidence levels

```text
STRONG
MEDIUM
WEAK
```

### STRONG examples

- Windows packaged activation returns/associates the expected AUMID/package identity and a matching game process is observed;
- process image resolves under validated install root and matches release/adapter expected executable identity after the launch handoff;
- client-specific supported evidence associates the external game ID with the process.

### MEDIUM examples

- expected executable name + expected install root + same user session + launch-time correlation;
- expected window/application identity + process creation after handoff.

### WEAK examples

- any new foreground process after launch;
- filename-only match without validated path/identity;
- timing-only correlation.

A normal supported launch MUST NOT reach `GAME_RUNNING` from WEAK evidence alone.

---

## 20. Game process identity

Where Win32 process identity is used:

```text
processIdentity = PID + creationTime
```

not PID alone.

Expected executable evidence MAY include:

```text
normalized image path
validated install root relationship
file identity/signature evidence where useful
known bootstrap/launcher role
```

Process binary hashes MUST NOT be universally required because game updates change binaries frequently.

---

## 21. Game session evidence

Adapter/observer emits:

```text
GAME_PROCESS_CANDIDATE
GAME_STARTING_CONFIRMED
GAME_RUNNING_CONFIRMED
GAME_PROCESS_REPLACED
GAME_EXITED_CONFIRMED
CORRELATION_LOST
START_TIMEOUT
```

`GAME_RUNNING_CONFIRMED` requires adapter-defined minimum strong/medium evidence and stability/readiness predicates.

A visible window MAY strengthen evidence but MUST NOT be universally mandatory because some games have long headless/bootstrap phases.

---

## 22. Start timeouts

Timeouts are policy/configuration values, not hard-coded universal game facts.

A launch timeout means:

```text
SplitOS did not prove GAME_RUNNING within allowed observation window
```

It does not prove that the external client failed internally.

Result:

```text
GAME_PROCESS_NOT_CONFIRMED
```

unless stronger client evidence supports another specific outcome.

---

## 23. Exit correlation

Exit is not:

```text
one PID exited
```

for games that use bootstrap/replacement processes.

Adapter/session observer maintains a correlated process set.

Normal exit confirmation:

```text
all required correlated game processes absent
+
no replacement candidate appears during exit grace period
↓
GAME_EXITED_CONFIRMED
```

Client process (Steam/Epic/Xbox/Battle.net) remaining alive is not a reason to keep Game Session running.

---

## 24. Already-running games

Before managed handoff, adapter MAY detect a strongly correlated existing game process.

Result:

```text
ALREADY_RUNNING_CONFIRMED
```

Only strong evidence may produce this result.

SplitOS MUST NOT attach a managed Game Session to an arbitrary same-name process from WEAK evidence.

---

## 25. Direct executable launch

Default policy:

```text
client-managed game
→ launch through owning client/platform mechanism
```

Directly starting the game EXE is forbidden as the generic fallback for Steam/Epic/Microsoft Gaming/Battle.net titles because it can bypass:

- authentication;
- license checks;
- platform environment;
- cloud/update preparation;
- required launcher/runtime context.

An exception requires an explicit per-game/platform supported integration contract.

---

## 26. Anti-cheat / DRM boundary

Adapters MUST NOT require:

```text
DLL injection
remote-thread injection
memory scanning of protected gameplay state
anti-cheat bypass
DRM modification
license emulation
network interception
synthetic cheating input
```

Process/window observation uses normal Windows evidence from SPEC-06 only.

---

## 27. Local metadata trust

Version-sensitive client files are parsed as hostile/untrusted input.

Mandatory parser rules:

- bounded file size;
- bounded recursion/nesting;
- explicit text encoding handling;
- schema/type checks;
- path canonicalization;
- no implicit command execution;
- no arbitrary URI parameters copied from metadata without validation;
- parser failure returns unknown/stale evidence, never partial invented truth.

---

## 28. Path trust

A local manifest may claim an install path.

Before storing as `validatedInstallRoot`, adapter MUST:

1. normalize to an absolute path;
2. reject invalid/traversal representations;
3. verify expected directory/file evidence where possible;
4. retain source/provenance;
5. never pass that path to Privileged Broker as an arbitrary filesystem command.

A validated path remains evidence, not permission.

---

## 29. Client compatibility record

Each adapter publishes:

```text
GameClientCompatibility
{
  adapterVersion
  clientType
  observedClientVersion?
  testedClientVersionRange?
  supportedWindowsBuildRange
  capabilityStatuses
  parserSchemaSignatures[]
  lastValidatedUtc
  knownBreakageCode?
}
```

If a known incompatible client version appears:

```text
CLIENT_VERSION_UNSUPPORTED
```

is preferable to silently running an obsolete parser.

---

## 30. Unknown/new client versions

Policy by capability:

```text
public documented protocol activation
→ may remain usable if registration is valid

version-sensitive local library parser
→ downgrade to BEST_EFFORT/UNKNOWN until schema validation passes
```

This permits launch capability to survive some client updates without pretending library parsing is still trusted.

---

## 31. Adapter isolation from canonical state

Adapter MUST NOT write:

```text
OperationalModeState
ModeTransition
GameSession canonical lifecycle
GameProfile
Entitlement
```

directly.

It emits evidence/results to owning modules.

```text
Adapter evidence
→ Game Library / Game Launch / Game Session owner
→ semantic decision
```

---

## 32. Failure handling

Expected adapter errors:

```text
CLIENT_NOT_INSTALLED
CLIENT_PROTOCOL_NOT_REGISTERED
CLIENT_VERSION_UNSUPPORTED
LIBRARY_SOURCE_NOT_FOUND
LIBRARY_PARSE_FAILED
LIBRARY_SCHEMA_UNKNOWN
STALE_PROJECTION
GAME_NOT_INSTALLED
LAUNCH_IDENTITY_MISSING
LAUNCH_HANDOFF_FAILED
CLIENT_INTERACTION_REQUIRED
GAME_PROCESS_NOT_CONFIRMED
CORRELATION_AMBIGUOUS
GAME_EXIT_NOT_CONFIRMED
```

No error here changes committed WORK/GAME mode automatically.

Normally:

```text
managed game launch failed
→ remain GAME
→ return/keep Game Launcher available
```

---

## 33. Observability

Every refresh/launch/correlation operation carries existing:

```text
correlationId
operationId
```

Adapter diagnostics include:

- adapter/client version;
- capability/mechanism status;
- external game identity (non-secret);
- evidence freshness;
- normalized result;
- parser/mechanism diagnostic code.

Must not include:

- external passwords;
- session cookies;
- platform auth tokens;
- full arbitrary launcher command lines containing secrets.

---

## 34. v1 implementation order

Recommended engineering order:

```text
1. Shared adapter contract + projection model
2. Steam prototype and compatibility tests
3. Epic protocol + local evidence prototype
4. Microsoft packaged/AUMID activation prototype
5. Battle.net research/prototype
6. cross-client process correlation harness
```

`TARGET_SUPPORTED_V1` becomes release support only after verification cases pass.

---

## 35. Normative invariants

### GC-INV-001
External client/platform remains authority for its account/license/install domain.

### GC-INV-002
Projection evidence always carries provenance and freshness.

### GC-INV-003
Public launch handoff success is not game-running proof.

### GC-INV-004
Weak timing/foreground evidence alone cannot establish `GAME_RUNNING`.

### GC-INV-005
Client adapters never require external account passwords/tokens.

### GC-INV-006
Client-local metadata never becomes direct privileged command input.

### GC-INV-007
Client update can invalidate one capability without invalidating all adapter capabilities.

### GC-INV-008
Direct game EXE launch is not the generic fallback for client-owned games.

### GC-INV-009
Adapter failure does not automatically change OperationalMode.

### GC-INV-010
No anti-cheat/DRM tampering is required for supported integration.

---

## 36. Result

The stable SplitOS model is:

```text
External Game Client
        ↓
client-specific, capability-rated mechanism
        ↓
Game Client Adapter
        ↓
normalized evidence / launch handoff
        ↓
Game Library / Game Launch / Game Session owners
        ↓
SplitOS canonical behavior
```

The next client-specific documents define how this contract is implemented per platform without weakening the shared semantics.