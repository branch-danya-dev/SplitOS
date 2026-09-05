# SPEC-13 — Local Telemetry Pipeline and Storage

## 1. Purpose

Defines how SplitOS structured diagnostic events are emitted, persisted, rotated and captured locally without turning diagnostics into a second canonical database.

---

## 2. Local pipeline

```text
Component
→ Diagnostic Event API
→ field privacy classification
→ structured event envelope
→ sink fan-out
     ├── local NDJSON sink
     ├── protected machine audit sink
     └── ETW/TraceLogging sink
```

A sink failure is reported separately and does not rewrite the original event semantics.

---

## 3. Shared diagnostic library

All SplitOS-owned processes SHOULD consume one versioned shared diagnostic contract/library containing:

- event envelope types;
- event registry/schema metadata;
- correlation propagation helpers;
- privacy field annotations;
- redaction primitives;
- local sink implementations;
- ETW provider helpers;
- diagnostic health counters.

This avoids every component inventing its own log schema.

The library is not a semantic owner of the emitted domain event.

---

## 4. Default structured sink

Default durable operational events use rotating NDJSON segments.

Conceptual record:

```text
one JSON object
+ newline
```

Properties:

- UTF-8;
- one event per line;
- append-oriented current segment;
- completed segments immutable to ordinary product operation;
- bounded by time and size;
- no requirement for a central database service.

---

## 5. Why not canonical SQLite

Canonical SQLite stores are optimized for owned state and durable transactions.

Diagnostics are intentionally different:

```text
machine.db / user.db
→ canonical state

Logs/*.ndjson
→ diagnostic evidence
```

A corrupt log segment must not make `machine.db` unavailable.

Deleting expired logs must not change product state.

---

## 6. Directory baseline

### Machine

```text
%ProgramData%\SplitOS\Logs\Machine\
    broker-YYYYMMDD-<segment>.ndjson
    bootstrap-YYYYMMDD-<segment>.ndjson
    recovery-YYYYMMDD-<segment>.ndjson
```

### Protected audit

```text
%ProgramData%\SplitOS\Logs\Audit\
    audit-YYYYMMDD-<segment>.ndjson
```

### User/session

```text
%LocalAppData%\SplitOS\Logs\User\
    runtime-YYYYMMDD-<segment>.ndjson
    manager-YYYYMMDD-<segment>.ndjson
    launcher-YYYYMMDD-<segment>.ndjson
```

Filename metadata is convenience only.

The record content determines identity/correlation.

---

## 7. Current-segment write semantics

The active log segment may be append-written.

On rotation:

```text
flush
→ close
→ optionally checksum/compress
→ rename/finalize
→ open next segment
```

An abrupt crash may leave the final line incomplete.

Consumers MUST tolerate a truncated final record and continue decoding prior complete records.

No recovery operation is allowed to interpret a partial log line as product state.

---

## 8. Rotation trigger

Segment rotation occurs on at least:

- maximum segment size;
- process restart/day boundary as implementation chooses;
- release/update transition where separating versions improves supportability.

Exact segment size is a performance gate.

A reasonable engineering starting point is 8–32 MiB segments; production value is decided in SPEC-14 performance validation.

---

## 9. Compression

Completed old operational segments MAY be compressed.

Rules:

- current active segment is not continuously recompressed;
- compression failure does not delete the source segment;
- diagnostic bundle tooling understands both raw/compressed segment forms;
- protected audit compression inherits the same ACL.

---

## 10. ACL baseline

### Machine logs

Normal writers are the relevant privileged SplitOS components.

Ordinary user processes must not receive direct write permission to protected machine log/audit roots.

### User logs

The current Windows user owns its own `%LocalAppData%` diagnostic boundary.

### Support tools

A support/export tool reads through explicit product capabilities or under the same user/privileged boundary appropriate to the source.

It does not grant a normal user generic write rights to machine audit.

---

## 11. Diagnostic health

The diagnostic subsystem tracks its own state:

```text
sinkAvailable
lastSuccessfulWriteUtc
lastRotationUtc
bytesUsed
recordsDropped
recordsRejectedPrivacy
etlSessionState
crashDumpCount
```

These are diagnostic-health facts, not product authority.

---

## 12. Backpressure

Observability MUST NOT create unbounded memory queues.

When sinks are slow:

### Critical security audit

Use bounded synchronous/durable behavior where the protected operation contract requires an audit record.

### Normal operational events

Use a bounded queue and record aggregate dropped-event evidence when possible.

### DEBUG/TRACE

May be dropped first.

Never block a gameplay hot path indefinitely waiting for a verbose diagnostic file write.

---

## 13. Dropped-event marker

If events are dropped due to buffer/storage pressure, the sink SHOULD emit an aggregate marker after recovery:

```text
Diagnostic.EventsDropped
fromUtc
toUtc
categoryCounts
reason
```

Do not pretend the timeline is complete.

---

## 14. ETW / TraceLogging provider model

SplitOS SHOULD expose named providers by responsibility boundary rather than one provider per class.

Candidate provider families:

```text
SplitOS.Runtime
SplitOS.Broker
SplitOS.Game
SplitOS.UpdateRecovery
SplitOS.Security
SplitOS.UI
```

Exact provider GUIDs/names are frozen before implementation release.

---

## 15. ETW default posture

Normal production runtime:

```text
important low-frequency TraceLogging events = allowed
detailed verbose providers = disabled unless capture enabled
```

An on-demand trace session can enable detailed providers dynamically without process restart.

This is especially valuable for:

- mode transition timing;
- display/device event races;
- game launch correlation;
- Broker latency;
- update/recovery investigation;
- performance regression analysis.

---

## 16. WPR/WPA compatibility

On-demand ETW capture SHOULD be consumable by standard Windows tracing tooling such as WPR/WPA where practical.

SplitOS may ship a bounded WPR profile or equivalent capture configuration for support.

A capture profile defines:

- SplitOS providers;
- selected Windows providers only when needed;
- buffer sizes;
- capture duration;
- maximum file size;
- privacy notice.

---

## 17. Continuous performance tracing prohibited by default

A high-volume ETW performance session MUST NOT run permanently merely because Game Mode is active.

Normal optimization telemetry from SPEC-08 is separately scoped and bounded.

Deep trace capture is explicit and time-bounded.

---

## 18. Process lifecycle

At process start emit:

```text
Runtime.Process.Started
componentVersion
releaseId
processInstanceId
PID evidence
windowsSessionId where applicable
```

At graceful stop:

```text
Runtime.Process.Stopping
reason
```

Unexpected death is usually observed by another component/SCM/WER; a process cannot reliably log after it has already crashed.

Therefore:

```text
no final log event
!= proof of crash cause
```

---

## 19. Release boundary

Every record includes product release/component version so mixed-version incidents after update/recovery can be reconstructed.

During side-by-side update, support may legitimately see:

```text
Bootstrap v1.5
Broker v1.4 stopping
Broker v1.5 starting
Runtime v1.5 starting
```

The timeline must not assume all processes share one version during mutation.

---

## 20. Clock behavior

Each component records UTC wall-clock plus monotonic durations for local operations.

For cross-process order, consumers use:

- occurred/observed timestamp;
- request/operation relationship;
- durable transaction state;
- event sequence within a process where available.

Clock timestamps alone are not sufficient to prove causal order in every race.

---

## 21. Diagnostic sink failure events

When possible:

```text
Diagnostic.Sink.Degraded
Diagnostic.Sink.Recovered
Diagnostic.Retention.CleanupFailed
Diagnostic.DiskPressure
```

If the only sink is broken, the subsystem may expose health through Manager/status IPC instead.

---

## 22. Disk budgeting

Diagnostics receives a bounded budget.

Default total budgets are split by scope rather than one global unlimited folder.

Operational logs are evictable according to retention.

Audit is protected by its own stricter policy.

Trace/dump artifacts are the first large artifacts considered for expiry under pressure.

---

## 23. No log-based replay engine

SplitOS does not implement:

```text
read events
→ replay state
→ set canonical mode/update/entitlement
```

Durable canonical transactions remain in the stores defined by SPEC-03/05/11.

Logs help humans/tools understand why those stores reached a state.

---

## 24. Verification

SPEC-14 must cover:

- concurrent user/machine logging;
- truncated final NDJSON record;
- rotation under crash;
- disk full;
- ACL denial;
- high-rate TraceLogging capture;
- no gameplay-blocking synchronous DEBUG sink;
- component-version/release tagging across self-update;
- support decoder compatibility with compressed/unknown events.
