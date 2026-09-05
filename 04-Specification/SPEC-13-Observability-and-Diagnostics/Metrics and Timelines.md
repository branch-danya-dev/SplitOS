# SPEC-13 — Metrics and Timelines

## 1. Purpose

Defines bounded v1 metrics and diagnostic timeline reconstruction for the major SplitOS flows.

Metrics summarize behavior. Timelines explain a concrete incident.

Neither becomes canonical product state.

---

## 2. Metric classes

### 2.1 Duration histograms

Examples:

```text
mode.transition.duration_ms
game.launch.duration_ms
broker.operation.duration_ms
update.stage.duration_ms
update.apply.duration_ms
recovery.duration_ms
```

### 2.2 Counters

Examples:

```text
mode.transition.result.count
game.launch.result.count
broker.authorization.denied.count
integration.error.count
update.result.count
recovery.result.count
diagnostic.events_dropped.count
```

### 2.3 Gauges / current health evidence

Examples:

```text
diagnostic.log_bytes
recovery_store_free_bytes
runtime.memory_working_set_bytes
runtime.active_capture
```

Current health metrics are evidence, not canonical state.

---

## 3. Metric label/cardinality rule

Metrics MUST NOT use unbounded user-specific labels.

Forbidden dimensions:

```text
full file path
account email
correlationId as metric label
game title for global always-on cardinality without bounded catalog
PID
requestId
```

Allowed bounded dimensions may include:

```text
component
result class
operation class
client type
mode target
failure phase
release channel
```

Concrete incident IDs belong in logs/traces, not metric dimensions.

---

## 4. Mode transition metrics

Minimum candidate set:

```text
mode.transition.duration_ms
mode.transition.result.count
mode.transition.blocker.count
mode.transition.rollback.count
mode.transition.verification_failure.count
```

Bounded dimensions:

```text
transitionKind = ACTIVATE|SWITCH|DEACTIVATE
source = NONE|WORK|GAME
target = NONE|WORK|GAME
outcome
```

---

## 5. Game launch metrics

```text
game.launch.total_duration_ms
game.launch.preparation_duration_ms
game.launch.handoff_duration_ms
game.launch.confirmation_duration_ms
game.launch.result.count
game.launch.correlation_ambiguous.count
```

Dimensions:

```text
clientType = STEAM|EPIC|MICROSOFT_GAMING|BATTLENET|OTHER_SUPPORTED
result class
```

Do not make every game title a permanent high-cardinality metric label.

---

## 6. Windows integration metrics

Useful bounded examples:

```text
windows.display.apply.duration_ms
windows.display.target_not_reached.count
windows.device.snapshot_invalidated.count
windows.service.apply.duration_ms
windows.power.apply.duration_ms
```

Raw device identifiers do not belong in metric labels.

---

## 7. Broker/security metrics

```text
broker.operation.duration_ms
broker.operation.result.count
broker.authorization.denied.count
broker.stale_fence.denied.count
trust.artifact_verification.failed.count
trust.metadata_refresh.failed.count
```

Security metrics never replace the detailed protected audit event.

---

## 8. Update/recovery metrics

```text
update.download.duration_ms
update.stage.duration_ms
update.recovery_capsule.duration_ms
update.apply.duration_ms
update.target_verify.duration_ms
update.result.count
update.rollback.count
recovery.duration_ms
recovery.result.count
```

Release version strings may be included in incident events; metrics SHOULD prefer bounded release channel/major compatibility classes where cardinality matters.

---

## 9. Observability health metrics

```text
diagnostic.event_write_failure.count
diagnostic.events_dropped.count
diagnostic.bundle.result.count
diagnostic.bundle.size_bytes
diagnostic.trace.capture.duration_ms
diagnostic.trace.limit_reached.count
diagnostic.crash_dump.count
```

These allow detection that the diagnostic subsystem itself is failing.

---

## 10. Local metric persistence

v1 does not require a permanent time-series database on every device.

Options:

```text
in-memory rolling counters/histograms
+
periodic bounded structured summary events
```

Detailed incident reconstruction comes from events/traces.

A local permanent TSDB is not part of the v1 baseline.

---

## 11. Remote metrics boundary

Because automatic cloud telemetry has not been accepted at requirements level, v1 local metrics are not automatically exported to a central service by SPEC-13.

If future product telemetry is introduced, the metric catalog is designed to map cleanly to OpenTelemetry metric concepts while retaining privacy/cardinality controls.

---

# 12. Timeline reconstruction

A timeline is a derived support representation:

```text
correlation/transaction IDs
+
ordered diagnostic events
+
selected canonical diagnostic views
→ incident timeline
```

It is not a replay log.

---

## 13. Mode timeline

Expected stages:

```text
request
→ access/precondition validation
→ blocker inspection
→ decisions
→ policy resolution
→ apply actions
→ read-back verification
→ commit OR rollback/fallback
```

Example:

```text
12:01:00.100 Requested GAME
12:01:00.130 Transition REQUESTED
12:01:00.500 Blocker scan complete
12:01:01.200 Display target resolved
12:01:01.400 SetDisplayConfig submitted
12:01:01.480 Actual target mismatch
12:01:01.490 Verification failed
12:01:01.520 Rollback started
12:01:02.100 WORK source verified
12:01:02.130 Terminal FAILED_SAFE_FALLBACK
```

This makes the difference between request/apply/read-back/commit visible.

---

## 14. Game launch timeline

```text
Launch requested
→ profile resolved
→ config recommendation/apply
→ client handoff
→ handoff accepted/rejected
→ process evidence candidates
→ running confirmed or timeout
→ exit evidence
→ Launcher restored
```

The timeline must preserve:

```text
HANDOFF_ACCEPTED
!= GAME_RUNNING
```

---

## 15. Update timeline

```text
discovery
→ trust/eligibility
→ download
→ artifact verify
→ staging
→ recovery capsule
→ mutation lease
→ apply
→ reboot/checkpoint
→ resume
→ target verify
→ commit
```

After reboot, the timeline may contain a new trace/process instance but the same update transaction ID.

---

## 16. Recovery timeline

```text
failure detected
→ RecoveryContext assembled
→ recovery authorization
→ target selected
→ artifact/capsule verify
→ recovery apply
→ actual-state verify
→ terminal safe result
```

If Windows-native recovery takes over, timeline records the handoff rather than claiming SplitOS completed Windows repair.

---

## 17. Security timeline

For a suspicious/failed privileged operation:

```text
request observed
→ OS caller evidence
→ capability/target validation
→ trust/session/fence checks
→ allow/deny
→ privileged operation if allowed
→ technical result
```

This timeline uses protected audit as primary evidence for authorization decisions.

---

## 18. Cross-process ordering

Events from multiple processes can share close timestamps.

Causal reconstruction should prioritize:

1. request/reply relationships;
2. correlation/operation/transaction identity;
3. per-process sequence where available;
4. occurred/observed timestamp;
5. domain-state evidence.

Do not assume millisecond timestamp sorting alone proves causality.

---

## 19. Sequence numbers

Each process MAY maintain monotonically increasing `processEventSequence` for emitted events.

Protected audit MAY additionally use `auditSequence` within the audit writer.

Sequence resets on process restart unless the audit contract explicitly defines a durable sequence.

A process-local sequence is not global ordering.

---

## 20. User-facing timeline

Manager diagnostics may show a simplified timeline:

```text
Switch to Game
✓ Profile selected
✓ TV detected
✕ TV mode could not be verified
✓ Returned safely to Work
```

The user view must not claim more certainty than diagnostic evidence supports.

Raw internal event names/error codes can be available in advanced details/export.

---

## 21. Performance budget

Metrics/event instrumentation itself is measurable.

SPEC-14 should establish budgets for:

```text
idle CPU overhead
idle memory overhead
normal event I/O
Game Mode hot-path overhead
on-demand trace overhead
bundle generation impact
```

If a metric requires unacceptable always-on cost, it becomes on-demand rather than violating existing NFR-PERF requirements.

---

## 22. Verification

SPEC-14 must prove:

- bounded metric cardinality;
- no IDs/emails/paths as unbounded labels;
- correct mode/game/update/recovery duration boundaries;
- timeline spans process restart/reboot using durable IDs;
- handoff vs running states are distinguishable;
- timeline remains partial/explicit when events are dropped;
- observability overhead stays within accepted release budget.
