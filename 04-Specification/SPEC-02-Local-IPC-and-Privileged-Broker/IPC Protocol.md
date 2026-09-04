# SPEC-02 — IPC Protocol

## 1. Scope

This document defines the v1 message protocol used by:

```text
Manager / Game Launcher ↔ Runtime Host
Runtime Host ↔ Privileged Broker
```

The trust level differs, but framing/version/correlation conventions are shared where practical.

---

## 2. Transport and framing

Transport: Windows Named Pipes.

Framing:

```text
uint32_le payloadLength
payloadLength bytes UTF-8 JSON
```

Constraints:

- maximum JSON payload length: `262144` bytes;
- frame length must be validated before payload allocation;
- multiple logical messages may use one connection;
- each frame contains exactly one JSON message object;
- compression is not supported in v1;
- binary blobs are not supported inline.

---

## 3. Protocol version

Protocol version:

```json
{
  "major": 1,
  "minor": 0
}
```

Rules:

- different major → no mutating communication;
- same major + compatible minor → allowed only for advertised message/capability set;
- unknown required field semantics → reject message;
- unknown optional extension fields MAY be ignored only if schema marks them forward-compatible.

---

## 4. Connection state machine

```text
CONNECTED
↓
WAIT_HELLO
├── invalid → REJECT / CLOSE
└── valid
    ↓
ESTABLISHED
    ├── request / response
    ├── query / response
    ├── event (Runtime UI pipe only)
    └── close
```

Broker MUST NOT process a privileged capability while in `WAIT_HELLO`.

---

## 5. Hello message

```json
{
  "type": "Hello",
  "protocol": { "major": 1, "minor": 0 },
  "componentRole": "RuntimeHost",
  "releaseVersion": "1.0.0",
  "instanceId": "opaque-uuid",
  "requestedFeatures": []
}
```

For Broker, the following fields are **claims**, not proof:

```text
componentRole
releaseVersion
instanceId
```

The Broker derives actual client PID/session and validates the executable independently.

---

## 6. HelloAck

```json
{
  "type": "HelloAck",
  "protocol": { "major": 1, "minor": 0 },
  "serverRole": "Broker",
  "serverReleaseVersion": "1.0.0",
  "sessionEligibility": "CONTROL_OWNER",
  "features": ["Broker.Health.Read"],
  "connectionId": "opaque-uuid"
}
```

Possible terminal handshake outcomes:

```text
INCOMPATIBLE_PROTOCOL
INVALID_CALLER
SESSION_NOT_CONTROL_OWNER
UNTRUSTED_COMPONENT
RELEASE_INCOMPATIBLE
```

---

## 7. Common request envelope

```json
{
  "type": "Request",
  "protocol": { "major": 1, "minor": 0 },
  "requestId": "uuid",
  "operationId": "uuid",
  "correlationId": "uuid",
  "capability": "Machine.ServicePolicy.Apply",
  "sentAtUtc": "2026-09-04T17:00:00Z",
  "deadlineUtc": "2026-09-04T17:00:15Z",
  "idempotencyKey": "uuid",
  "payload": {}
}
```

### Required fields

| Field | Required | Meaning |
|---|---:|---|
| `type` | yes | message kind |
| `protocol` | yes | protocol major/minor |
| `requestId` | yes | one concrete transport request |
| `operationId` | yes | one semantic operation attempt |
| `correlationId` | yes | end-to-end user/product chain |
| `capability` | yes | allowlisted capability ID |
| `sentAtUtc` | yes | diagnostic ordering/evidence |
| `deadlineUtc` | yes | caller execution deadline |
| `idempotencyKey` | mutations only | retry deduplication identity |
| `payload` | yes | capability-specific strict schema |

Broker ignores any caller-supplied identity field as proof.

---

## 8. Response envelope

```json
{
  "type": "Response",
  "requestId": "uuid",
  "operationId": "uuid",
  "status": "OK",
  "errorCode": null,
  "messageCode": null,
  "result": {},
  "observedAtUtc": "2026-09-04T17:00:01Z"
}
```

### Status values

```text
OK
ACCEPTED
REJECTED
DENIED
INVALID_REQUEST
INCOMPATIBLE_PROTOCOL
BUSY
TIMEOUT
NOT_SUPPORTED
FAILED
TARGET_NOT_VERIFIED
INTERRUPTED
```

`messageCode` is intended for stable product/localization mapping; free-form error strings are diagnostic only and MUST NOT be parsed by callers for behavior.

---

## 9. Error code namespaces

Recommended namespaces:

```text
IPC_*        framing/protocol
AUTHZ_*      caller/capability authorization
SESSION_*    Windows/control-session rules
BROKER_*     broker internal execution
WINDOWS_*    bounded Windows operation result
UPDATE_*     maintenance technical result
RECOVERY_*   recovery technical result
```

Examples:

```text
IPC_FRAME_TOO_LARGE
IPC_SCHEMA_INVALID
AUTHZ_CALLER_NOT_RUNTIME_HOST
SESSION_NOT_CONTROL_OWNER
BROKER_CAPABILITY_UNKNOWN
BROKER_IDEMPOTENCY_CONFLICT
WINDOWS_TARGET_NOT_REACHED
```

---

## 10. Runtime UI pipe messages

Manager/Game Launcher communicate only with Runtime Host semantic contracts.

UI commands use `command` instead of Broker `capability`, for example:

```text
Mode.RequestTransition
Game.RequestLaunch
Game.QueryLibrary
Profile.Save
Account.OpenUpgrade
Runtime.QuerySnapshot
```

UI protocol MUST NOT expose Broker capability IDs as a pass-through mechanism.

Bad:

```text
Manager
→ RuntimeHost.ExecuteBrokerCapability(name, payload)
```

Required:

```text
Manager
→ semantic command
→ owning Runtime module validates
→ module decides whether Broker capability is required
```

---

## 11. Runtime UI events

Runtime Host MAY publish events to connected UI clients.

Example:

```json
{
  "type": "Event",
  "eventId": "uuid",
  "correlationId": "uuid",
  "name": "Mode.CommittedChanged",
  "sequence": 124,
  "occurredAtUtc": "...",
  "payload": {}
}
```

Events are presentation synchronization.

UI loss of an event MUST be recoverable by querying a current snapshot.

Therefore events are not the only source of truth for UI reconstruction.

---

## 12. Sequence numbers

Runtime UI event streams SHOULD expose a monotonically increasing per-RuntimeHost `sequence` number.

If client observes a gap:

```text
sequence gap
→ discard assumption of complete event history
→ query RuntimeUiSnapshot
```

Sequence resets after Runtime Host restart are allowed and signaled by changed `instanceId`.

---

## 13. Time and deadlines

`sentAtUtc` is diagnostic evidence and MUST NOT be used as strong security freshness proof by itself.

`deadlineUtc` is evaluated against current system time.

For local IPC:

```text
expired before dispatch
→ TIMEOUT / DEADLINE_EXPIRED
```

If mutation already began, expiry does not imply rollback or cancellation; capability-specific semantics decide.

---

## 14. Idempotency behavior

State-changing request examples:

```text
Machine.ServicePolicy.Apply
Machine.Policy.Apply
Maintenance.Update.ApplyVerified
Maintenance.Recovery.Execute
```

must include `idempotencyKey`.

Broker behavior:

```text
first key
→ execute
→ remember technical result/reference

same key + same normalized capability/payload
→ return prior known technical result/reference

same key + different capability/payload
→ REJECT + BROKER_IDEMPOTENCY_CONFLICT
```

Durable cross-reboot idempotency is owned by domain transaction IDs in later specs.

---

## 15. Payload normalization

Before comparing an idempotency replay, implementation MUST compare semantic normalized payload, not raw JSON byte ordering.

Example:

```json
{"a":1,"b":2}
```

and

```json
{"b":2,"a":1}
```

are semantically equivalent if schema says so.

---

## 16. Schema rules

Each message/capability MUST have a versioned schema.

Schema requirements:

- explicit field types;
- explicit required/optional fields;
- bounded string/array lengths;
- enums reject unknown dangerous values;
- paths/identifiers validated according to capability;
- no polymorphic arbitrary object deserialization based on caller-supplied type name;
- no executable code/script fields.

---

## 17. Long-running job pattern

Start:

```json
{
  "capability": "Maintenance.Update.ApplyVerified",
  "payload": { "transactionId": "...", "stagedArtifactId": "..." }
}
```

Response:

```json
{
  "status": "ACCEPTED",
  "result": { "brokerJobId": "..." }
}
```

Then:

```text
Maintenance.Job.Query(brokerJobId)
```

Broker technical job progress is evidence only; Update/Recovery semantic owners still decide transaction state.

---

## 18. Cancellation protocol

```json
{
  "type": "CancelRequest",
  "requestId": "new-uuid",
  "operationId": "target-operation-id",
  "correlationId": "...",
  "targetRequestId": "..."
}
```

Outcomes:

```text
CANCELLED
CANCEL_NOT_AVAILABLE
ALREADY_COMPLETED
UNKNOWN_OPERATION
```

`CANCELLED` may be returned only when capability semantics confirm no further mutation will continue from that operation.

---

## 19. Connection loss

Connection loss means:

```text
transport state unknown/closed
```

not:

```text
operation failed
```

For an in-flight mutation:

1. reconnect;
2. use `operationId`/`idempotencyKey`/job query;
3. read actual state if needed;
4. let semantic owner reconcile.

---

## 20. Backpressure

Server MUST bound:

- simultaneous pipe instances;
- pending frames per connection;
- payload size;
- long-running job count;
- event queue per UI client.

When capacity is exceeded, semantic result is `BUSY`, not unbounded memory growth.

Exact concurrency numeric limits are implementation tuning/verification inputs and may differ by capability.

---

## 21. Security-sensitive parsing

Protocol implementation MUST:

- parse with deterministic strict JSON settings;
- reject duplicate security-critical fields where parser ambiguity could exist;
- avoid type-name-based object creation;
- validate length before decoding;
- keep secrets out of logs;
- avoid logging whole privileged payloads by default.

---

## 22. Compatibility tests

Minimum protocol verification cases:

```text
same major/minor normal request
same major/newer compatible minor
major mismatch
unknown capability
malformed JSON
invalid UTF-8
oversized frame
missing required field
duplicate mutation replay
idempotency conflict
expired deadline
connection drop during mutation
UI event sequence gap
```
