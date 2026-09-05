# SPEC-13 — Security Audit Contract

## 1. Purpose

Defines the minimum protected audit record for security-sensitive SplitOS decisions and privileged machine mutations.

Audit supports investigation and accountability.

It does not create a new authorization authority.

---

## 2. Scope

Audit is mandatory for selected actions including:

```text
Broker caller validation
Broker capability authorization
privileged machine mutation
update/recovery privileged activation
TUF/release trust rejection
Authenticode publisher rejection
recovery authorization decision
security-floor/revocation state change
protected diagnostic capture/export action
```

Ordinary high-volume gameplay events are not security audit solely because they are important.

---

## 3. Audit event envelope

Audit reuses the SPEC-13 event envelope with required security fields.

Conceptual example:

```json
{
  "eventName": "Broker.Capability.Decision",
  "category": "SECURITY_AUDIT",
  "severity": "INFO",
  "correlation": {
    "correlationId": "...",
    "operationId": "...",
    "requestId": "...",
    "windowsSessionId": 3
  },
  "payload": {
    "capability": "Machine.ServicePolicy.Apply",
    "callerPid": 1234,
    "callerProcessInstanceEvidence": "...",
    "callerSidClass": "CURRENT_CONSOLE_USER",
    "callerValidation": "PASSED",
    "authorization": "ALLOWED",
    "targetId": "SEARCH_INDEXER",
    "fenceToken": 52
  }
}
```

Exact raw SID/user names are not required when a stable privacy-safe classification is sufficient.

---

## 4. Caller identity evidence

The Broker audit SHOULD distinguish:

```text
claimed identity
!= OS-observed identity
```

Audit records the OS-observed caller facts used by authorization, e.g.:

- actual client PID;
- actual Windows session ID;
- token/user/logon class;
- active console ownership check;
- binary/path/publisher provenance result where required;
- protocol/capability validation result.

Never record reusable authentication secrets.

---

## 5. Authorization result

Canonical values:

```text
ALLOWED
DENIED_IDENTITY
DENIED_SESSION
DENIED_PROVENANCE
DENIED_CAPABILITY
DENIED_TARGET
DENIED_STALE_FENCE
DENIED_PROTOCOL
DENIED_TRUST
INDETERMINATE
```

`INDETERMINATE` is not implicitly allowed.

---

## 6. Mutation audit

For a privileged mutation, audit should make it possible to relate:

```text
authorization decision
→ operation start
→ technical result
→ read-back evidence
→ semantic verification result elsewhere
```

Broker audit does not claim the final semantic mode/update success.

Example:

```text
Broker.Operation.Result = SUCCESS
```

still does not mean:

```text
OperationalMode = GAME
```

---

## 7. Release trust audit

Security audit records at least failures and significant trust transitions for:

```text
TUF Root update
metadata expiration/freeze/rollback rejection
release target digest mismatch
Authenticode publisher failure
securityEpoch/releaseSequence denial
revocation metadata applied
RecoveryAuthorization allow/deny
```

Do not log private keys, certificate private material or entire sensitive token payloads.

---

## 8. Entitlement/auth audit boundary

Authentication diagnostics may record:

```text
auth flow started/completed/failed
issuer
result code
account association result
entitlement refresh result class
```

They MUST NOT record:

```text
access token
refresh token
authorization code
PKCE verifier
password
cookie
Authorization header
```

Account IDs in exported bundles are pseudonymized unless exact support handling explicitly requires otherwise.

---

## 9. Update/recovery audit

Protected update/recovery decisions should include:

```text
sourceRelease
targetRelease
releaseSequence/securityEpoch
mutation lease/fence
RecoveryAuthorization ID/result
capsule verification result
commit durability result
```

This allows investigation of a downgrade/recovery incident without treating the audit log as the installed-release owner.

---

## 10. Audit writer

Machine security audit is written by the privileged component responsible for the decision or through a protected audit writer library under that security context.

Ordinary Manager/Game Launcher/Runtime user processes cannot directly append forged Broker audit records to the protected machine audit path.

---

## 11. ACL baseline

`%ProgramData%\SplitOS\Logs\Audit`:

```text
SYSTEM                     required control
Administrators             administrative/support control
NT SERVICE\SplitOSBroker   write/read as required
ordinary Users             no direct write
```

Exact installed SDDL follows the same hardening validation discipline as SPEC-02/SPEC-03.

---

## 12. Audit availability policy

For some capabilities, recording a protected audit record may be a precondition to mutation.

Examples likely to require audit availability:

```text
update activation
recovery restore
release security-floor mutation
privileged arbitrary machine policy class changes
```

For lower-risk routine bounded actions, an audit sink degradation may be allowed while recording a diagnostic health warning.

The capability catalog MUST declare whether audit durability is:

```text
REQUIRED_BEFORE_ACTION
REQUIRED_RESULT_RECORD
BEST_EFFORT
```

This prevents one global ambiguous policy.

---

## 13. Audit record order

Where audit must precede action:

```text
authorization accepted
→ durable audit intent/decision
→ privileged mutation
→ result audit
```

Where only result audit is required, exact ordering may differ.

No component should claim a result record was durable if the write failed.

---

## 14. Audit integrity limits

The protected ACL provides a meaningful boundary against ordinary user processes.

v1 security scope does not claim immutable/tamper-proof logging against:

```text
unrestricted local Administrator
kernel compromise
offline disk attacker outside disk-encryption guarantees
```

Optional record hash chaining remains an engineering enhancement, not a substitute for this threat statement.

---

## 15. Audit retention

Initial v1 policy:

```text
30 days
OR
128 MiB
```

whichever limit requires rotation first, subject to preserving current active incident records and SPEC-14 validation.

Retention cleanup itself emits an aggregate audit/diagnostic event where possible.

---

## 16. Export filtering

Default diagnostic bundle does not automatically include every audit record from the machine.

It includes:

- records correlated to the selected incident/time range;
- security failures relevant to that incident;
- release/recovery trust evidence relevant to the incident.

Unrelated account/security activity is excluded.

---

## 17. Sensitive target representation

Audit logs bounded semantic identifiers rather than arbitrary raw mutation strings.

Good:

```text
capability = Machine.ServicePolicy.Apply
targetId = SEARCH_INDEXER
```

Avoid:

```text
registryPath = arbitrary user supplied path
commandLine = arbitrary shell command
```

This aligns diagnostics with the Broker capability model.

---

## 18. Denial visibility

Repeated denials MAY be rate-aggregated to prevent log flooding, but the first/relevant denial must preserve:

```text
what was requested
why it was denied
caller/session classification
correlation
```

Rate limiting must not convert denial into allow.

---

## 19. Verification cases

SPEC-14 must prove:

- ordinary user cannot forge protected audit file;
- Broker denial produces correct reason code;
- stale fencing denial is auditable;
- secrets are absent under successful/failed auth paths;
- update/recovery decisions include release/security context;
- audit unavailable policy behaves per capability declaration;
- export includes only incident-relevant audit subset by default.
