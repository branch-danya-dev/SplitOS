# SPEC-13 — Observability & Diagnostics

## 1. Purpose

This package defines the implementable observability, diagnostic, crash-artifact, privacy, retention and support-export contracts for SplitOS v1.

It refines the existing requirements:

```text
NFR-OBS-001..005
NFR-DATA-100..102
```

and preserves the A&D invariant:

```text
DiagnosticRecord
!= canonical system truth
```

Observability exists to explain what happened. It does not become a second owner of mode, entitlement, update, game session, release or recovery state.

---

## 2. Scope

SPEC-13 defines:

- a stable SplitOS diagnostic event model;
- cross-process / cross-boundary correlation identifiers;
- event categories and severity semantics;
- local structured logging;
- ETW / TraceLogging integration for detailed traces;
- performance metrics and timelines;
- privileged/security audit records;
- crash dump collection policy;
- diagnostic bundle generation;
- redaction and privacy classification;
- retention and disk-pressure behavior;
- user-initiated support export;
- boundaries for future remote/cloud telemetry.

SPEC-13 does not define:

- canonical business/system state ownership;
- a SaaS observability vendor;
- automatic cloud telemetry product policy;
- support-ticket backend implementation;
- SIEM vendor integration;
- final operational SLOs/alert thresholds;
- arbitrary memory/process inspection.

---

## 3. Normative source chain

```text
NFR-OBS-001..005
+
NFR-DATA-100..102
↓
A&D Observability & DiagnosticRecord semantics
↓
SPEC-01 correlation/process model
SPEC-02 IPC request/operation/correlation IDs
SPEC-05 Mode transaction model
SPEC-07 Game launch/session correlation
SPEC-11 Update/Recovery transaction model
SPEC-12 release/security audit requirements
↓
SPEC-13
```

If a diagnostic record conflicts with canonical persisted state:

```text
log mismatch
→ diagnostic investigation
!= automatic truth replacement
```

---

## 4. Observability planes

SplitOS separates three planes.

### 4.1 Operational Events

Structured product events describing:

```text
user intent
operation start/end
state transition attempts
external integration results
Windows evidence
verification results
failures/degradation
```

Examples:

```text
Mode.Transition.Started
Mode.Transition.VerificationFailed
Game.Launch.HandoffAccepted
Game.Session.RunningConfirmed
Update.Transaction.StateChanged
Recovery.TargetVerified
```

### 4.2 Security Audit

Security-sensitive records describing:

```text
privileged capability request
caller identity evidence
trust validation
allow/deny decision
release verification
recovery authorization
key/revocation state consumed
```

Audit is not a verbose debug log. It is a bounded record of sensitive decisions.

### 4.3 Diagnostic Artifacts

Larger or specialized artifacts:

```text
ETW trace (.etl)
WER crash dump (.dmp)
diagnostic bundle (.zip)
explicit support capture
```

These artifacts have stronger privacy and retention controls than normal structured events.

---

## 5. v1 local-first principle

The default v1 observability model is local-first.

```text
local product behavior
→ local structured events / protected audit / optional local trace
```

NFR-DATA-102 explicitly requires separate requirements before cloud telemetry/personalization data exchange is introduced.

Therefore v1 MUST NOT silently enable continuous remote upload of:

- game library data;
- hardware inventories;
- process lists;
- crash dumps;
- raw ETW traces;
- diagnostic event streams.

Remote transfer in v1 is limited to an explicitly user-initiated support/export action unless a later requirements decision introduces a product telemetry service.

---

## 6. Event identity model

Every structured event has:

```text
schemaVersion
eventName
eventVersion
eventId
occurredAtUtc
observedAtUtc
severity
category
component
componentVersion
processInstanceId
releaseId
windowsBuild
correlation context
outcome / error data
privacy classification
payload
```

The event is immutable after emission.

A sink may enrich transport metadata but MUST NOT reinterpret the event into canonical state.

---

## 7. Correlation hierarchy

Existing identifiers remain distinct.

```text
correlationId
→ whole end-to-end user/system flow

operationId
→ one semantic operation inside the flow

transactionId
→ durable transaction identity when one exists

requestId
→ one IPC/HTTP request attempt

traceId / spanId
→ observability execution context

processInstanceId
→ one process lifetime
```

Example:

```text
User launches Cyberpunk from WORK

correlationId C1
│
├── Mode SWITCH operation O1
│   └── ModeTransitionId T1
│       ├── Broker request R1
│       └── Broker request R2
│
└── Game Launch operation O2
    └── GameLaunchId G1
        └── Steam handoff request R3
```

The same `correlationId` travels across Manager/Launcher → Runtime Host → Broker/backend/adapters where the interface supports it.

---

## 8. Event categories

Canonical v1 categories:

```text
LIFECYCLE
USER_ACTION
STATE_TRANSITION
WINDOWS_EVIDENCE
EXTERNAL_INTEGRATION
PERFORMANCE
FAILURE
RECOVERY
SECURITY_AUDIT
RELEASE_TRUST
DATA_MIGRATION
DIAGNOSTIC
```

Category is semantic. It is not equivalent to severity.

---

## 9. Severity

```text
TRACE
DEBUG
INFO
WARN
ERROR
CRITICAL
```

Rules:

- `INFO` is normal significant lifecycle/product behavior;
- `WARN` is degraded/unexpected but still controlled behavior;
- `ERROR` means an operation/required capability failed;
- `CRITICAL` means system invariant / recovery / release-security condition requiring immediate attention;
- `DEBUG/TRACE` MUST be suppressible and MUST NOT be required for correctness.

A normal user cancellation is not `ERROR` merely because the operation did not complete.

---

## 10. Outcome semantics

Where applicable, events use explicit semantic outcome:

```text
SUCCESS
CANCELLED
DEFERRED
DENIED
DEGRADED
FAILED
FAILED_SAFE_FALLBACK
INDETERMINATE
```

Do not infer outcome from English message text.

---

## 11. Minimum v1 event families

### Runtime / process lifecycle

```text
Runtime.Process.Started
Runtime.Process.Stopping
Runtime.Process.CrashedObserved
Runtime.Session.ControlOwnershipChanged
Runtime.Reconciliation.Started
Runtime.Reconciliation.Completed
```

### Mode runtime

```text
Mode.Transition.Requested
Mode.Transition.BlockerDetected
Mode.Transition.UserDecision
Mode.Transition.StateChanged
Mode.Policy.Resolved
Mode.Action.Result
Mode.Verification.Result
Mode.Commit.Result
Mode.Rollback.Result
```

### Windows context

```text
Windows.Display.Resolve.Result
Windows.Display.Apply.Result
Windows.Audio.Resolve.Result
Windows.Input.DeviceChanged
Windows.Power.Apply.Result
Windows.Service.Apply.Result
Windows.Hardware.GenerationInvalidated
```

### Gaming

```text
Game.Library.Reconciliation.Result
Game.Profile.Resolution.Result
Game.Optimization.Recommendation
Game.Configuration.Apply.Result
Game.Launch.Requested
Game.Launch.Handoff.Result
Game.Session.RunningConfirmed
Game.Session.ExitConfirmed
SharedApp.Presentation.Result
```

### Update / recovery

```text
Update.Discovery.Result
Update.Artifact.Verification.Result
Update.RecoveryCapsule.Result
Update.Transaction.StateChanged
Update.Target.Verification.Result
Update.Commit.Result
Recovery.Transaction.StateChanged
Recovery.Target.Verification.Result
Recovery.Result
```

### Trust / Broker

```text
Broker.Caller.Validation
Broker.Capability.Decision
Broker.Operation.Result
Trust.Metadata.Refresh.Result
Trust.Artifact.Verification.Result
Trust.RecoveryAuthorization.Result
Trust.RevocationApplied
```

### Diagnostics

```text
Diagnostic.Capture.Started
Diagnostic.Capture.Completed
Diagnostic.Bundle.Created
Diagnostic.Bundle.ExportCompleted
Diagnostic.Redaction.Failed
Diagnostic.Retention.Cleanup
```

---

## 12. Technology baseline

### 12.1 Structured local records

The product emits a SplitOS-defined structured event envelope independent of sink technology.

Default durable local sink:

```text
newline-delimited JSON (NDJSON)
UTF-8
rotating segments
atomic segment finalization
bounded retention
```

Rationale:

- readable without a proprietary database tool;
- streamable;
- easy to include selectively in support bundles;
- a broken diagnostic file does not corrupt canonical SQLite stores;
- logs remain clearly separate from canonical data.

### 12.2 ETW / TraceLogging

Windows-native detailed tracing uses ETW / TraceLogging providers.

ETW is used for:

- dynamic verbose diagnostic sessions;
- low-overhead detailed event capture;
- performance diagnosis;
- correlation with Windows/provider traces;
- WPR/WPA-compatible investigation.

ETW trace collection is not the only durable product log.

### 12.3 OpenTelemetry compatibility

For future backend/support telemetry or service-side flows, SplitOS event context SHOULD be mappable to OpenTelemetry concepts:

```text
Resource
TraceId
SpanId
LogRecord
Metric
```

This is a semantic interoperability target, not a mandate to run an OpenTelemetry Collector on every SplitOS PC.

---

## 13. Physical local layout

Extends SPEC-03 reserved `Logs` paths.

```text
%ProgramData%\SplitOS\
├── Logs\
│   ├── Machine\
│   ├── Audit\
│   └── Traces\
└── Diagnostics\
    ├── CrashDumps\
    ├── BundleStaging\
    └── SupportCapture\

%LocalAppData%\SplitOS\
├── Logs\
│   └── User\
└── Diagnostics\
    ├── CrashDumps\
    └── BundleStaging\
```

Paths MUST be resolved from Windows Known Folders, not hard-coded `C:\` assumptions.

Machine audit and Broker crash artifacts use protected machine ACLs.

---

## 14. Writer boundaries

### Runtime Host

Writes user/session operational events and user-scope diagnostic artifacts.

### Privileged Broker

Writes machine privileged-operation audit and machine-scope operational events.

### Manager / Game Launcher

May emit UI/presentation events through their own user-scope providers/logs, but MUST NOT write machine audit directly.

### Update Bootstrap / WinRE Recovery Tool

Write bounded machine/recovery events to protected recovery-capable diagnostic storage.

No component may use the diagnostic channel to mutate canonical state.

---

## 15. Security audit baseline

Sensitive decisions MUST emit audit records for at least:

- Broker caller validation;
- Broker capability allow/deny;
- machine mutation start/result;
- update/recovery privileged apply;
- release/TUF metadata trust failure;
- Authenticode publisher failure;
- recovery authorization allow/deny;
- security-floor/revocation change;
- protected diagnostic capture request.

Audit records MUST include enough identity/correlation evidence to investigate the decision but MUST exclude reusable secrets.

Audit is protected against ordinary interactive-user modification by ACL boundary.

v1 does not claim tamper-proof audit against an unrestricted local Administrator/kernel attacker.

---

## 16. Crash baseline

WER LocalDumps is the primary v1 Windows-native crash-dump mechanism for SplitOS user-mode processes.

Default:

```text
per-application configuration
DumpType = MiniDump
bounded DumpCount
SplitOS-owned local folder
no automatic network upload
```

Full-memory dumps are not normal default diagnostics.

A temporary deep-diagnostics capture MAY enable a fuller dump for a named SplitOS process after explicit user/support action and clear privacy/storage warning.

Crash dump presence never proves the cause of failure by itself.

---

## 17. Diagnostic bundle

User/support can request a bundle for a defined incident/time range.

Conceptual bundle:

```text
DiagnosticBundle.zip
├── manifest.json
├── summary.json
├── events/
├── audit/                 # filtered/redacted relevant subset
├── transactions/
├── environment/
├── compatibility/
├── traces/                # optional
└── crashes/               # optional, explicit
```

A bundle is a snapshot/export artifact.

It is not imported as canonical state.

---

## 18. Default bundle principle

Default bundle SHOULD contain enough context to satisfy NFR-OBS-003:

```text
what failed
when
target state
actual state evidence
affected component
recovery result
```

It SHOULD NOT dump the user's whole machine state, whole game library or unrestricted process history when the incident does not require it.

---

## 19. Privacy classification

Every event field/artifact belongs to one class:

```text
PUBLIC_PRODUCT
PSEUDONYMOUS_DIAGNOSTIC
USER_ENVIRONMENT
SENSITIVE
SECRET_FORBIDDEN
```

`SECRET_FORBIDDEN` includes at least:

- passwords;
- refresh/access tokens;
- Authorization/Cookie headers;
- private signing keys;
- payment card data;
- raw entitlement signing secrets;
- arbitrary document content.

These fields MUST NOT be logged.

---

## 20. Path / identity redaction

Default exported bundle normalizes or redacts:

```text
C:\Users\Alice\...
→ %USERPROFILE%\...

accountId
installationId
external client account IDs
→ bundle-scoped pseudonymous identifiers when exact value is not required

HTTP query strings / headers
→ allowlisted diagnostic fields only
```

Stable device identifiers/serials are omitted unless a specific incident capture explicitly requires them.

---

## 21. Remote telemetry boundary

Automatic continuous cloud telemetry is not enabled by this specification.

Allowed v1 remote flows:

```text
User explicitly chooses Export
→ local bundle only
```

or, if support-upload UI is implemented:

```text
User explicitly chooses Send to SplitOS Support
→ review summary / consent
→ bounded upload of selected bundle
```

Future automatic telemetry requires separate product/privacy requirements.

---

## 22. Retention baseline

Initial v1 local defaults, subject to SPEC-14 performance/disk validation:

```text
Operational structured logs
→ 14 days OR 256 MiB per scope, whichever first

Security audit
→ 30 days OR 128 MiB, whichever first

On-demand ETW traces
→ 24 hours after capture OR explicit export

Crash minidumps
→ max 5 per process, normally max 7 days

Bundle staging
→ remove after successful export or within 24 hours
```

A user-exported bundle outside SplitOS staging becomes user-owned and is not silently deleted by SplitOS.

---

## 23. Disk-pressure behavior

Priority:

```text
canonical state
> recovery safety
> protected audit
> operational diagnostics
> verbose trace artifacts
```

Under disk pressure:

1. delete expired verbose traces;
2. delete expired diagnostic staging;
3. rotate ordinary operational logs;
4. rotate audit only according to its policy;
5. never delete canonical DB or Recovery Capsule merely to preserve logs.

A diagnostic write failure MUST NOT be reported as successful diagnostic persistence.

It also MUST NOT normally block unrelated Windows usability.

---

## 24. Performance overhead

Observability must respect existing NFR performance requirements.

Default runtime mode:

```text
important structured events = enabled
security audit = enabled
verbose DEBUG/TRACE = disabled
continuous ETW performance trace = disabled
```

Detailed trace capture is explicitly time-bounded.

High-frequency performance events SHOULD use ETW/TraceLogging rather than synchronous verbose file writes on gameplay hot paths.

---

## 25. Event schema/versioning

Each named event has a stable schema contract.

Rules:

- `eventName` semantic meaning MUST NOT change incompatibly in place;
- compatible added optional fields may increment `eventVersion` according to registry policy;
- removed/reinterpreted required fields require a new event version/name;
- unknown fields are ignored by older bundle tooling;
- unknown event versions remain exportable even if not semantically decoded.

---

## 26. Diagnostic event registry

Production event schemas are release-owned versioned knowledge.

The registry defines:

```text
eventName
eventVersion
category
severity default
field names/types
privacy class per field
required correlation fields
sampling/verbosity policy
```

Arbitrary free-form string-only logging is insufficient for normative system events.

Human-readable `message` MAY exist as a convenience field but is never the semantic API.

---

## 27. Failure behavior

Observability failures are locally contained where possible.

Examples:

```text
user log path unavailable
→ event sink degraded
→ critical operation continues if correctness does not depend on log

security audit path unavailable before privileged mutation
→ operation policy may deny/defer if audit is mandatory for that capability

ETW trace start failed
→ support capture degraded
→ product runtime remains usable

bundle redaction failed
→ export denied
→ raw bundle is not silently sent
```

---

## 28. Time semantics

Events carry UTC timestamps and, where needed, monotonic duration measurements.

```text
occurredAtUtc
observedAtUtc
monotonicDurationNs / elapsedMs
```

Wall-clock movement MUST NOT produce negative operation duration.

Security trust time (SPEC-12 `trustedTimeFloorUtc`) is a separate authority concept; a log timestamp cannot advance or reset it.

---

## 29. Minimum timelines recoverable from diagnostics

Support tooling MUST be able to reconstruct, when relevant events survived retention:

```text
Mode transition timeline
Game launch/session timeline
Update transaction timeline
Recovery timeline
Broker privileged-operation timeline
Release trust-verification timeline
```

Reconstruction is diagnostic interpretation, not canonical replay.

---

## 30. Standards / platform basis

v1 mechanisms align with:

- Windows Event Tracing for Windows (ETW);
- Windows TraceLogging for self-describing structured tracing;
- Windows Error Reporting LocalDumps for user-mode crash artifacts;
- OpenTelemetry log/trace correlation concepts for backend interoperability.

Exact log parser/viewer/support backend vendor remains an implementation choice.

---

## 31. SPEC decisions

### SPEC-DEC-146

SplitOS uses a local-first observability model in v1; continuous remote telemetry is not introduced by SPEC-13.

### SPEC-DEC-147

Operational events, security audit and heavy diagnostic artifacts are separate observability planes.

### SPEC-DEC-148

Normative significant events use a versioned structured event envelope; free-form message text is supplementary only.

### SPEC-DEC-149

Existing correlation/operation/transaction/request IDs keep distinct meanings and are propagated rather than collapsed into one generic ID.

### SPEC-DEC-150

NDJSON rotating local segments are the default durable operational diagnostic sink, separate from canonical SQLite stores.

### SPEC-DEC-151

ETW/TraceLogging is the Windows-native detailed/high-frequency tracing substrate.

### SPEC-DEC-152

Security-sensitive machine decisions produce protected machine-scope audit records.

### SPEC-DEC-153

WER LocalDumps is the primary v1 crash-dump mechanism; minidump is default and full dump is explicit deep diagnostics only.

### SPEC-DEC-154

Diagnostic export is selective/redacted by default and never imports back into canonical state.

### SPEC-DEC-155

Secrets are forbidden from diagnostic records; redaction failure blocks remote/export transfer rather than silently leaking raw data.

### SPEC-DEC-156

Retention is bounded by both time and size; diagnostics never win disk space over canonical state/recovery safety.

### SPEC-DEC-157

OpenTelemetry concepts are an interoperability mapping, not a requirement to run a permanent OTel collector locally.

---

## 32. Open engineering gates

- exact NDJSON segment size and compression threshold after performance testing;
- exact ETW provider GUID/name allocation;
- whether all .NET components use EventSource/TraceLogging integration or a thin shared native/provider wrapper;
- exact WER custom minidump flags if default MiniDump proves insufficient;
- support-upload transport/backend after separate privacy/product decision;
- whether security audit needs an additional hash-chain integrity marker for accidental/tamper detection below Administrator threat;
- exact bundle encryption-at-rest/transport policy for future support upload;
- exact SLO/alert thresholds;
- whether user-facing local log viewer ships in v1.

---

## 33. Artifacts

```text
SPEC-13-Observability-and-Diagnostics/
├── README.md
├── Event Model and Correlation.md
├── Local Telemetry Pipeline and Storage.md
├── Security Audit Contract.md
├── Crash and Trace Capture.md
├── Diagnostic Bundle and Support Export.md
├── Privacy Redaction and Retention.md
├── Metrics and Timelines.md
├── SPEC-13 Traceability.md
├── observability-pipeline.mmd
├── diagnostic-bundle.mmd
└── correlation-timeline.mmd
```

---

## 34. Handoff to SPEC-14

SPEC-14 must turn this contract into executable acceptance cases, including:

- cross-process correlation preservation;
- event-schema compatibility;
- no-secret logging tests;
- log rotation/disk-full tests;
- audit permission tests;
- WER crash dump generation;
- redaction tests;
- bundle reproducibility/content tests;
- failure to export on redaction failure;
- observability overhead benchmarks;
- update/recovery timeline reconstruction tests.
