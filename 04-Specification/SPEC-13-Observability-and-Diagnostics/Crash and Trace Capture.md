# SPEC-13 — Crash and Trace Capture

## 1. Purpose

Defines the supported v1 mechanisms for capturing crash artifacts and time-bounded detailed traces without turning every system into a permanent high-volume recorder.

---

## 2. Crash artifact baseline

Primary v1 crash mechanism:

```text
Windows Error Reporting LocalDumps
```

for SplitOS user-mode executables.

Target processes include at least:

```text
SplitOS.RuntimeHost.exe
SplitOS.Manager.exe
SplitOS.GameLauncher.exe
SplitOS.Broker.Service.exe
SplitOS.UpdateBootstrap.exe
SplitOS Recovery Tool process where WER is available
```

---

## 3. Default dump type

Default production posture:

```text
DumpType = 1  # MiniDump
```

Rationale:

- bounded size;
- lower privacy exposure than full process memory;
- enough for first-line crash stack/module/thread investigation in many cases;
- full dumps remain available as explicit deep-diagnostic escalation.

Exact custom minidump flags may replace default `DumpType=1` only after SPEC-14 proves a materially better support/value tradeoff.

---

## 4. Full dump policy

Full process dumps may contain:

- strings from user files;
- access tokens or transient credentials present in memory;
- game/client metadata;
- URLs;
- application buffers;
- sensitive configuration.

Therefore:

```text
Full dump
→ NOT default
→ explicit support/deep-diagnostics action
→ named process
→ bounded duration/count
→ clear privacy/storage warning
```

A full dump is never automatically uploaded.

---

## 5. WER configuration ownership

Per-application WER LocalDumps configuration is installed/managed through trusted SplitOS machine configuration paths.

It is not edited by ordinary UI through arbitrary registry writes.

If configuration mutation requires privilege, it uses a typed Broker/setup capability.

---

## 6. Dump folder

Machine/service dump target:

```text
%ProgramData%\SplitOS\Diagnostics\CrashDumps\Machine
```

User-session process dump target:

```text
%LocalAppData%\SplitOS\Diagnostics\CrashDumps
```

Exact WER behavior for service identities must be validated on target Windows build and ACLs must allow the crashing process/WER to create the file without granting broad user write access to machine dumps.

---

## 7. Count/retention

Baseline:

```text
max 5 dumps per SplitOS process
normally max age 7 days
```

Large deep-diagnostic dumps may use a stricter count/space limit.

Cleanup never deletes a dump actively selected for an in-progress user export.

---

## 8. Crash identity

A dump index record SHOULD include:

```text
crashArtifactId
component
componentVersion
releaseId
processInstanceId if known
PID evidence
createdAtUtc
sizeBytes
dumpType
sha256
related correlationId if recoverable
related operation/transaction IDs if recoverable
```

This index is diagnostic metadata, not a crash-cause verdict.

---

## 9. Crash observation

A process usually cannot reliably emit a final event after it faults.

Crash evidence may come from:

```text
WER dump presence
SCM service exit
parent/supervisor observation
next-start reconciliation
Windows application error evidence
```

Do not invent:

```text
last log line
→ crash cause
```

The last event may only be the last successfully flushed event.

---

## 10. Application Recovery API

Windows Application Recovery/Restart APIs MAY be evaluated for selected interactive UI processes where preserving transient UI state is useful.

They are not a replacement for the canonical SplitOS transaction/recovery model.

For Runtime Host/Broker update/mode correctness:

```text
SPEC-03/05/11 durable state
→ recovery truth
```

not an application crash callback.

---

## 11. ETW trace capture

Detailed trace mode captures ETW/TraceLogging into `.etl`.

Conceptual flow:

```text
User/support starts Diagnostic Capture
↓
select capture profile
↓
start bounded ETW session
↓
reproduce problem
↓
stop capture
↓
finalize ETL
↓
redaction/export eligibility metadata
```

---

## 12. Capture profiles

Candidate v1 profiles:

```text
MODE_TRANSITION
GAME_LAUNCH
DISPLAY_DEVICE
UPDATE_RECOVERY
BROKER_SECURITY
GENERAL_RUNTIME
```

A profile defines providers and limits.

It does not grant new runtime privileges to the observed product action.

---

## 13. Windows provider capture

A support profile MAY include selected Windows ETW providers when needed for root-cause analysis.

Examples could include relevant process, display/device, power or servicing providers.

However the exact Windows-provider list is version-gated engineering knowledge.

Do not enable broad kernel/network/file tracing by default without privacy/performance justification.

---

## 14. Capture limits

Each on-demand capture specifies:

```text
maxDuration
maxFileSize
provider set
verbosity
circular/file mode
privacy warning level
```

If a capture hits its limit:

```text
CAPTURE_LIMIT_REACHED
```

is recorded and the session is stopped/finalized rather than growing without bound.

---

## 15. Gameplay capture

During GAME_RUNNING, detailed tracing must be performance-conscious.

Use ETW buffering rather than synchronous per-frame text logging.

A capture profile must not enable an unbounded set of expensive providers merely because the incident concerns a game.

SPEC-14 must benchmark worst-case supported capture overhead.

---

## 16. PresentMon relationship

SPEC-08 performance telemetry and SPEC-13 trace capture are separate concerns.

```text
PresentMon-compatible performance sample
→ optimization evidence

ETW diagnostic capture
→ support/root-cause timeline
```

They may be correlated but one does not automatically replace the other.

---

## 17. Trace artifact metadata

Each trace artifact has:

```text
traceArtifactId
captureProfile
startedAtUtc
endedAtUtc
releaseId
component versions
correlation/transaction filters if any
size
sha256
capture truncation/limit status
privacy classification
```

---

## 18. Raw trace export

Raw ETL may contain more environmental information than a filtered structured log bundle.

Therefore default support bundle behavior:

```text
raw ETL inclusion = optional / explicit
```

If included, the bundle manifest states that it contains detailed system trace data.

---

## 19. Dump export

Default bundle MAY reference available dump existence without embedding it.

Embedding a dump requires explicit selection.

Example UX semantics:

```text
Include crash dump (may contain process-memory data)
[ ]
```

A support upload cannot silently add a dump after the user reviewed the bundle summary.

---

## 20. Artifact integrity

Before export:

```text
finalize artifact
→ compute size + SHA-256
→ add manifest entry
```

Artifact hash provides bundle integrity/reference, not product release authority.

---

## 21. Artifact cleanup

Temporary trace/dump capture artifacts follow retention policy.

Cleanup must not:

- delete a file while WER/ETW still writes it;
- follow arbitrary symlinks/reparse points outside SplitOS diagnostic roots;
- delete user-exported bundles outside staging;
- delete canonical product/recovery data.

---

## 22. Crash-loop handling

If a component repeatedly crashes, dump generation itself must remain bounded.

```text
crash loop
→ max dump count reached
→ oldest eligible dump rotated
→ aggregate crash-loop diagnostic evidence
```

Do not fill the system disk with identical full dumps.

---

## 23. Security boundary

Crash/trace mechanisms are diagnostic capabilities, not arbitrary process-dump APIs.

User-facing SplitOS tooling MUST NOT offer:

```text
Dump arbitrary process by PID
Trace arbitrary secrets/network traffic
Read arbitrary process memory
```

Deep capture is scoped to SplitOS-owned/supported diagnostic targets and approved capture profiles.

---

## 24. Verification

SPEC-14 must test:

- RuntimeHost crash creates expected minidump;
- Broker service crash dump ACL/location;
- max count cleanup;
- crash loop does not exhaust disk;
- full dump requires explicit deep mode;
- ETW capture starts/stops without app restart;
- capture limit enforcement;
- trace after system clock adjustment;
- bundle does not include raw dump/ETL unless selected.
