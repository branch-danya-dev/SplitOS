# SPEC-02 — Local IPC & Privileged Broker Specification

Status: **READY FOR REVIEW**  
Transport decision: **Windows Named Pipes for v1**.  
Scope: local process IPC, Broker service boundary, authorization, protocol behavior and privileged capability surface.

---

## 1. Purpose

This specification closes the v1 local IPC transport decision and defines the privileged execution boundary.

Core architecture:

```text
Manager / Game Launcher
        ↓ unprivileged session IPC
Runtime Host
        ↓ privileged Broker IPC
Privileged Broker
        ↓ allowlisted machine operation
Windows
```

The Broker is an executor of bounded privileged capabilities. It is not a semantic owner of Work/Game/account/update meaning.

---

## 2. Transport decision

v1 MUST use Windows Named Pipes for local IPC.

Reasons:

- product is Windows-only;
- pipe objects support Windows security descriptors/DACLs;
- local service ↔ user-session duplex communication is supported;
- Windows can expose client process/session identity to pipe server;
- remote clients can be rejected at pipe creation;
- no local TCP listener/firewall/network identity is required.

The implementation MUST NOT silently replace the Broker transport with localhost HTTP/TCP without updating this specification and Trust analysis.

---

## 3. Pipe families

There are two distinct IPC trust boundaries.

### 3.1 UI → Runtime Host

One per Windows session:

```text
\\.\pipe\SplitOS.Runtime.v1.S<SessionId>
```

Server: `SplitOS.RuntimeHost.exe`  
Clients: `SplitOS.Manager.exe`, `SplitOS.GameLauncher.exe`

This pipe carries semantic UI commands, queries and state events.

### 3.2 Runtime Host → Privileged Broker

Broker exposes a per-session endpoint only for the currently eligible physical-console session:

```text
\\.\pipe\SplitOS.Broker.v1.S<SessionId>
```

Server: `SplitOS.Broker.Service.exe`  
Client role allowed: `SplitOS.RuntimeHost.exe` only.

Manager and Game Launcher MUST NOT connect directly to Broker.

---

## 4. Pipe creation security

Every pipe MUST use an explicit security descriptor.

Default Named Pipe security MUST NOT be relied upon.

### 4.1 Remote clients

Pipe server creation MUST specify behavior equivalent to:

```text
PIPE_REJECT_REMOTE_CLIENTS
```

### 4.2 Runtime pipe DACL

The Runtime Host pipe DACL MUST permit only:

- current session logon SID / intended current user context;
- LocalSystem for support/diagnostic cases only if explicitly needed by implementation.

It MUST NOT grant generic Everyone/Anonymous access.

### 4.3 Broker pipe DACL

For `SplitOS.Broker.v1.S<SessionId>`, Broker MUST construct DACL from the target console session's logon SID plus LocalSystem/service identity as required.

This is defense-in-depth; DACL admission alone is not sufficient caller authorization.

---

## 5. Broker connection validation

On every accepted Broker connection, Broker MUST independently derive and validate the caller.

Required validation chain:

```text
pipe DACL admitted connection
→ GetNamedPipeClientSessionId
→ GetNamedPipeClientProcessId
→ open client process/token
→ verify session == pipe target session
→ verify session == current eligible physical console session
→ verify user/logon SID expected for that session
→ verify executable identity/path/release trust
→ verify protocol handshake
→ authorize capability
```

Claimed `processId`, `sessionId`, `userSid` or executable path from JSON payload MUST NOT be treated as authority.

### 5.1 Process identity

Broker SHOULD hold a process handle long enough to validate the connected client and reduce PID-reuse ambiguity.

The Runtime Host process image MUST satisfy:

- expected installed protected path;
- trusted SplitOS release-signing policy;
- expected component role from local release manifest;
- compatible release/protocol.

Exact release signer/key validation is owned by `SPEC-12`.

### 5.2 Session identity

Broker MUST compare the OS-derived client SessionId to the Broker pipe's session and to the current physical-console eligibility policy.

Mismatch outcome:

```text
DENIED / SESSION_NOT_CONTROL_OWNER
```

---

## 6. Broker service baseline

```text
Service name: SplitOSBroker
Binary: SplitOS.Broker.Service.exe
SCM account: LocalSystem (v1)
Start: Automatic
Session: 0
Process: dedicated service process
```

### 6.1 Why LocalSystem

v1 Broker performs protected machine-scoped operations across services/policies/update/recovery surfaces.

LocalSystem is therefore selected deliberately for the first implementation baseline, not as a default convenience.

Because LocalSystem has broad rights, the following hardening controls are mandatory.

---

## 7. Broker hardening requirements

Broker MUST:

1. run in a dedicated process;
2. enable a service SID (`NT SERVICE\SplitOSBroker`) for service-specific ACL use;
3. configure required privileges through SCM where practical rather than relying on every LocalSystem privilege;
4. expose no arbitrary command/script/shell interface;
5. expose no generic raw Registry/file/service mutation interface;
6. have no product need for outbound network access;
7. never store SplitOS account refresh/access tokens;
8. keep update/recovery artifact validation tied to signed/digest-bound metadata;
9. validate Runtime Host caller identity on each new connection;
10. audit privileged requests/results without secrets;
11. use protected installation directory/file ACLs;
12. reject unsupported protocol major versions;
13. reject requests from non-console-owner sessions for machine-wide v1 mutations.

### 7.1 Service SID type

v1 MUST enable a service SID.

`SERVICE_SID_TYPE_UNRESTRICTED` is the compatibility baseline for the first broker prototype.

`SERVICE_SID_TYPE_RESTRICTED` SHOULD be evaluated in ENG-06. Migration to RESTRICTED is preferred if all required resource ACLs and Windows operations remain functional.

The prototype MUST document which protected resources would need explicit ACL grants before such a migration.

### 7.2 Required privileges

Installer/service configuration SHOULD set `SERVICE_CONFIG_REQUIRED_PRIVILEGES_INFO` to the smallest tested set.

The exact privilege list is an ENG-06 implementation-validation output because it depends on concrete operations from `SPEC-06`, `SPEC-10`, and `SPEC-11`.

Absence of the final list does not authorize arbitrary privilege use.

### 7.3 Service failure actions

Broker failure actions MUST use restart-only behavior according to SPEC-01.

No `RUN_COMMAND` shell recovery and no automatic reboot for ordinary Broker crash.

---

## 8. Protocol framing

v1 wire format is length-prefixed UTF-8 JSON.

Frame:

```text
4-byte unsigned little-endian payload length
N bytes UTF-8 JSON
```

Rules:

- maximum frame payload: **262144 bytes (256 KiB)**;
- zero-length frame is invalid;
- oversized frame is rejected before allocation of unbounded buffers;
- invalid UTF-8 or malformed JSON closes/rejects the request;
- schema validation occurs before capability dispatch.

Large artifacts are never transferred inline; requests reference previously staged, verified artifact IDs/digests.

---

## 9. Protocol handshake

First logical message on a connection MUST be `Hello`.

Example semantic shape:

```json
{
  "type": "Hello",
  "protocol": { "major": 1, "minor": 0 },
  "componentRole": "RuntimeHost",
  "releaseVersion": "...",
  "capabilitiesRequested": []
}
```

For Broker, `componentRole` and version claims are hints only; Broker independently verifies process identity.

Broker responds:

```json
{
  "type": "HelloAck",
  "protocol": { "major": 1, "minor": 0 },
  "serverReleaseVersion": "...",
  "capabilitiesAdvertised": [],
  "sessionEligibility": "CONTROL_OWNER"
}
```

or a terminal handshake rejection.

No privileged request may be processed before successful handshake.

---

## 10. Request envelope

All request messages MUST contain:

```text
type
protocolMajor
requestId
operationId
correlationId
capability
sentAtUtc
deadlineUtc
idempotencyKey      required for mutation
payload
```

### 10.1 Identifier semantics

- `requestId` — unique concrete transport request;
- `operationId` — semantic owner operation/transaction attempt;
- `correlationId` — end-to-end product/user action chain;
- `idempotencyKey` — duplicate-mutation protection.

### 10.2 Deadline

Broker MUST reject a request whose deadline has already expired.

A deadline limits request execution intent; it does not permit Broker to lie about a mutation that may already have crossed a non-cancelable point.

---

## 11. Response envelope

Response MUST include:

```text
type
requestId
operationId
status
errorCode/messageCode if applicable
result payload if applicable
observedAtUtc
```

Canonical status family:

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

A technical `OK` describes the Broker capability result only. Semantic owners still decide whether their scenario may commit.

---

## 12. Long-running operations

Long update/recovery operations MUST NOT require one pipe call to remain open for the entire operation.

Pattern:

```text
StartX(request)
→ ACCEPTED + brokerJobId / transaction reference

QueryX(jobId)
→ current technical status/evidence
```

Durable semantic transaction ownership remains in Update/Recovery modules and their persistent records.

Broker job identity does not replace `UpdateTransactionRecord` or `RecoveryContext`.

---

## 13. Cancellation

Capabilities are explicitly classified:

```text
CANCELABLE
CANCELABLE_UNTIL_APPLY
NOT_CANCELABLE
```

Cancellation request:

```text
CancelOperation(operationId/requestId)
```

If an operation crossed its capability-defined non-cancelable point, Broker returns:

```text
CANCEL_NOT_AVAILABLE
```

It MUST NOT report cancellation while continuing a hidden mutation as if nothing happened.

---

## 14. Idempotency

Every state-changing privileged request MUST carry `idempotencyKey`.

Rules:

1. duplicate key + equivalent normalized request → return/reconstruct prior known result where possible;
2. duplicate key + different payload/capability → `IDEMPOTENCY_CONFLICT`;
3. restart-sensitive durable operations MUST rely on their domain transaction IDs/stores, not only Broker memory;
4. Broker in-memory deduplication is defense against immediate retry bugs, not the durable transaction source of truth.

---

## 15. Concurrency

Broker MUST NOT assume that connection ordering equals semantic operation ordering.

Runtime Host owns scenario ordering.

Broker MUST still reject conflicting privileged mutations when a lower-level resource cannot be safely mutated concurrently.

Outcome:

```text
BUSY
```

not silent interleaving.

Major transaction conflict semantics are further specified in `SPEC-05` and `SPEC-11`.

---

## 16. Capability authorization

Broker dispatches only known capability IDs from a compiled/versioned catalog.

Authorization inputs include:

- validated caller role/process;
- validated Windows session;
- physical-console control ownership where required;
- protocol version;
- capability-specific payload schema;
- local release/baseline compatibility;
- transaction/context requirements.

No capability may be inferred from a raw executable command or registry path supplied by the caller.

See `Broker Capability Catalog.md`.

---

## 17. Audit requirements

Every privileged request MUST emit diagnostic/audit evidence containing at minimum:

```text
timestamp
requestId
operationId
correlationId
client SessionId
client user/logon identity reference
validated process identity/release
capability
accept/deny/result code
duration
```

Audit MUST NOT include:

- account access/refresh tokens;
- payment secrets;
- arbitrary document contents;
- sensitive payload fields not required for support.

Audit is evidence, not canonical state.

---

## 18. Error handling

### Invalid caller

```text
close/reject connection
→ audit DENIED
→ no capability dispatch
```

### Malformed frame/schema

```text
INVALID_REQUEST
→ no mutation
```

### Protocol major mismatch

```text
INCOMPATIBLE_PROTOCOL
→ no mutating requests
```

### Broker internal failure before mutation

```text
FAILED
→ no semantic success
```

### Broker uncertainty after partial mutation

```text
TARGET_NOT_VERIFIED / INTERRUPTED
→ owning Runtime module reads actual state
→ rollback/recovery if required
```

Broker MUST NOT manufacture a safe canonical state.

---

## 19. Explicitly forbidden Broker surface

The following capabilities/contracts MUST NOT exist:

```text
RunCommand(commandLine)
RunPowerShell(script)
ExecuteFile(path, args)
WriteRegistry(key, value)
DeleteRegistryTree(key)
StartService(serviceName)
StopService(serviceName)
DeleteFile(path)
WriteFile(path, bytes)
KillProcess(pid)
SetArbitraryPrivilege(...)
```

Equivalent generic wrappers are also prohibited.

Required machine mutations use typed allowlisted domain-neutral execution capabilities with bounded identifiers.

---

## 20. Trust boundary after compromise assumptions

The design aims to resist an ordinary same-user malicious process from simply connecting and issuing privileged Broker commands.

Defense includes:

```text
per-session DACL
+ remote rejection
+ OS-derived client PID/session
+ user/logon validation
+ expected Runtime Host image path
+ release/signature validation
+ protocol/capability allowlist
```

This does not claim resistance to an unrestricted hostile local Administrator, kernel compromise or a compromised trusted Runtime Host binary, consistent with the Trust model.

---

## 21. Acceptance criteria

An implementation conforms only if:

1. Named Pipes are used for v1 local runtime/Broker IPC;
2. every pipe has explicit DACL and rejects remote clients;
3. Broker creates/authorizes a per-session endpoint and independently derives client SessionId/PID;
4. only validated Runtime Host may call Broker;
5. UI apps cannot call Broker directly;
6. protocol has explicit major/minor handshake;
7. framing is bounded length-prefixed UTF-8 JSON;
8. all mutations carry idempotency identity;
9. no arbitrary command/script/raw admin surface exists;
10. Broker runs as dedicated LocalSystem service with mandatory hardening controls;
11. Broker technical result never directly commits Work/Game/update semantic state;
12. audit/correlation data is emitted without secrets.

---

## 22. Engineering evidence

Windows APIs/mechanisms validated for this specification:

- Named Pipe security/DACL: https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights
- `PIPE_REJECT_REMOTE_CLIENTS`: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea
- `GetNamedPipeClientProcessId`: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid
- `GetNamedPipeClientSessionId`: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientsessionid
- Named Pipe client impersonation: https://learn.microsoft.com/en-us/windows/win32/ipc/impersonating-a-named-pipe-client
- Service SID: https://learn.microsoft.com/en-us/windows/win32/api/winsvc/ns-winsvc-service_sid_info
- LocalSystem security characteristics: https://learn.microsoft.com/en-us/windows/win32/services/localsystem-account
- `ChangeServiceConfig2` hardening/failure options: https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-changeserviceconfig2w
