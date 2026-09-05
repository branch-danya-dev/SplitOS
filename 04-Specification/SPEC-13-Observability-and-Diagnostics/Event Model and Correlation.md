# SPEC-13 — Event Model and Correlation

## 1. Purpose

Defines the canonical diagnostic event envelope and cross-component correlation model.

The event model is intentionally independent from one logging framework.

```text
semantic event
→ one or more sinks
```

not:

```text
logger API call
→ semantic truth
```

---

## 2. Event envelope

Conceptual v1 envelope:

```json
{
  "schemaVersion": 1,
  "eventName": "Mode.Transition.StateChanged",
  "eventVersion": 1,
  "eventId": "uuid",
  "occurredAtUtc": "2026-09-05T03:00:00.000Z",
  "observedAtUtc": "2026-09-05T03:00:00.003Z",
  "severity": "INFO",
  "category": "STATE_TRANSITION",
  "component": "SplitOS.RuntimeHost",
  "componentVersion": "1.5.0",
  "processInstanceId": "uuid",
  "releaseId": "splitos-1.5.0",
  "windowsBuild": "26100.x",
  "correlation": {},
  "outcome": null,
  "error": null,
  "payload": {}
}
```

Exact serialization belongs to shared diagnostic library implementation, but field semantics are normative.

---

## 3. Stable identity fields

### `eventId`

Unique occurrence identity.

It does not imply retry/idempotency semantics.

### `eventName`

Stable semantic event family.

### `eventVersion`

Schema version for the named event.

### `processInstanceId`

Generated once at process startup and reused for the process lifetime.

PID may also be recorded as diagnostic evidence but is not sufficient lifetime identity due PID reuse.

---

## 4. Correlation fields

Conceptual correlation object:

```json
{
  "correlationId": "...",
  "operationId": "...",
  "transactionId": "...",
  "requestId": "...",
  "traceId": "...",
  "spanId": "...",
  "windowsSessionId": 3,
  "gameLaunchId": "...",
  "modeTransitionId": "...",
  "updateTransactionId": "...",
  "recoveryId": "..."
}
```

Not every event contains every field.

---

## 5. Correlation rules

### 5.1 `correlationId`

Represents the widest meaningful end-to-end flow.

Examples:

```text
Switch to GAME
Launch game from WORK
Install SplitOS update
Recover previous release
```

A downstream component MUST preserve the incoming correlation ID unless it intentionally starts an unrelated flow.

### 5.2 `operationId`

Represents one semantic operation within a correlation flow.

Examples:

```text
Mode switch operation
Game launch operation
Display apply operation
Update apply operation
```

### 5.3 `transactionId`

Only used where a durable transaction exists.

It MUST refer to the durable identity already owned by that transaction model, not a second diagnostic-generated transaction ID.

### 5.4 `requestId`

One request attempt across IPC/HTTP boundary.

Retries use a new request ID while retaining the relevant operation/correlation context.

### 5.5 `traceId` / `spanId`

Execution-trace context.

It can be propagated across local IPC and backend HTTP calls.

It is an observability context and is never a business/system authority.

---

## 6. Propagation examples

### Manager → Runtime Host

```text
Manager request
correlationId C1
operationId O1
requestId R1
↓
Runtime Host
```

Runtime may create a trace span but MUST preserve `C1` and `O1`.

### Runtime Host → Broker

```text
C1 / O1
↓
Broker request R2
↓
Broker audit/result events
```

Broker does not trust the caller merely because the caller supplied a correlation ID.

### Runtime Host → Account Backend

```text
correlationId C2
operationId entitlement-refresh
requestId R3
traceId T1
spanId S1
↓
HTTPS backend request
```

Backend correlation headers MUST NOT contain secrets.

---

## 7. Parent-child execution relationship

If traces are used:

```text
traceId = same flow execution trace
spanId = current segment
parentSpanId = caller segment when known
```

The product-level `correlationId` remains useful even when a trace is broken, sampled, unavailable or spans a durable reboot boundary.

Therefore:

```text
correlationId
!= traceId
```

---

## 8. Reboot boundary

Durable transaction flows can survive reboot while an in-memory tracing span cannot.

Example:

```text
UpdateTransactionId U1
correlationId C9

Before reboot:
traceId T1

After reboot:
traceId T2
```

Both halves still correlate through:

```text
U1 + C9
```

Do not force a single in-memory trace span to survive reboot artificially.

---

## 9. Event timestamp semantics

### `occurredAtUtc`

Best-known source event time.

### `observedAtUtc`

Time the diagnostic system observed/accepted the record.

External evidence may have different source and observed timestamps.

Example:

```text
Steam process actually appeared at T1
Runtime observed it at T2
```

Both may be useful.

### Duration

Operation durations SHOULD use monotonic clocks internally.

Wall-clock changes MUST NOT produce negative duration.

---

## 10. Error object

Errors are structured.

```json
{
  "errorCode": "TARGET_STATE_NOT_REACHED",
  "errorType": "ModeVerificationFailure",
  "source": "DisplayContext",
  "retryability": "RETRYABLE_AFTER_CONTEXT_REFRESH",
  "failurePhase": "VERIFY",
  "safeState": "WORK",
  "message": "optional human readable text"
}
```

No parser may depend on the free-form message to determine error semantics.

---

## 11. Outcome vs error

Possible examples:

```text
Outcome = CANCELLED
Error = null
```

for user cancellation.

```text
Outcome = DENIED
Error = BROKER_AUTHORIZATION_REJECTED
```

for security denial.

```text
Outcome = FAILED_SAFE_FALLBACK
Error = TARGET_STATE_NOT_REACHED
```

for a failed operation that converged safely.

---

## 12. State events

A diagnostic state event MUST record both semantic names where useful:

```text
sourceState
requestedTarget
observedActualState evidence
resultingCanonicalState when committed
```

but MUST make the evidence/authority distinction explicit.

Example:

```json
{
  "sourceCommittedMode": "WORK",
  "targetMode": "GAME",
  "actualDisplayResult": "FAILED",
  "canonicalModeAfter": "WORK"
}
```

A logging consumer must not infer `GAME` solely because `targetMode=GAME` appeared.

---

## 13. User decision events

User decisions MAY be recorded as semantic action identifiers:

```text
CANCEL_TRANSITION
CLOSE_GAME_AND_CONTINUE
KEEP_EXTERNAL_SETTING
RESTORE_SPLITOS_RECOMMENDATION
```

Do not log arbitrary text typed by the user unless the feature explicitly requires it and privacy classification permits it.

---

## 14. External identifiers

External IDs are logged only when diagnostically needed.

Preferred:

```text
Steam App ID
Epic artifact/catalog identity
PFN/AUMID
SplitOS GameId
```

Avoid external account IDs/usernames.

File system paths are subject to redaction rules.

---

## 15. Event schema compatibility

A shared registry records event definitions.

Compatible evolution:

```text
add optional field
```

Potentially incompatible:

```text
change type
change meaning
make optional field required for old event version
reuse an old field for different semantics
```

Incompatible evolution requires a new event version.

---

## 16. No stringly-typed normative telemetry

Forbidden as the only record:

```text
"Game mode failed somehow"
```

Required semantic representation:

```text
eventName = Mode.Transition.VerificationFailed
errorCode = TARGET_STATE_NOT_REACHED
targetMode = GAME
sourceCommittedMode = WORK
failedPredicate = DISPLAY_TARGET_REACHED
```

A human-readable message may accompany it.

---

## 17. Activity / ETW mapping

When using ETW/TraceLogging:

- correlation/activity IDs SHOULD map to ETW activity concepts where practical;
- named typed fields remain the source representation;
- provider/event names are release-versioned contracts;
- a decoder does not require a separate external manifest for TraceLogging events.

---

## 18. OpenTelemetry mapping

Where remote/service-side OpenTelemetry is later used:

```text
component/componentVersion
→ Resource attributes

traceId/spanId
→ OTel Trace Context

event envelope
→ OTel LogRecord / event attributes

operation duration
→ span or histogram according to use case
```

Do not copy every local field blindly to remote telemetry.

Privacy policy is evaluated before export.

---

## 19. Minimum correlation verification

SPEC-14 must prove at least:

1. Manager → Runtime → Broker retains correlation ID;
2. each retry gets a new request ID;
3. durable transaction ID survives process restart/reboot;
4. process instance changes after crash/restart;
5. Game launch from WORK keeps one outer correlation across FL-02 + FL-03;
6. no trust decision accepts caller identity from correlation fields;
7. diagnostic parser handles unknown optional fields/event versions safely.
