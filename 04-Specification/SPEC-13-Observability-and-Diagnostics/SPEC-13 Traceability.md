# SPEC-13 — Traceability

## 1. Purpose

Maps requirements and prior architecture/specification decisions into the observability/diagnostics contracts defined by SPEC-13.

---

## 2. Requirement mapping

| Requirement | SPEC-13 coverage |
|---|---|
| `NFR-OBS-001` significant actions diagnosable | Event Model; Local Telemetry Pipeline; Metrics and Timelines |
| `NFR-OBS-002` minimum action families logged | README §11; Event Model; Metrics and Timelines |
| `NFR-OBS-003` error context | structured error/outcome; diagnostic bundle summary/timelines |
| `NFR-OBS-004` no unnecessary sensitive data | Privacy Redaction and Retention; Security Audit Contract |
| `NFR-OBS-005` diagnostic package export | Diagnostic Bundle and Support Export |
| `NFR-DATA-100` data minimization | privacy classes + bounded environment/process/game data |
| `NFR-DATA-101` local discovery != external transmission | local-first default; no automatic remote upload |
| `NFR-DATA-102` cloud telemetry requires separate requirements | remote telemetry explicitly out of default v1 |
| `NFR-PERF-001..005` background overhead | ETW on-demand detail; DEBUG/TRACE disabled by default; bounded sinks |
| `NFR-SEC-001/002` privilege minimization | machine audit writer/ACL; no generic dump/trace capability |
| `NFR-UPD-200..203` update safety/data | update/recovery timelines and diagnostic bundle views |
| `NFR-REC-001..006` recovery diagnosability | recovery timeline, WinRE diagnostic export |

---

## 3. A&D data/ownership mapping

### DiagnosticRecord

A&D rule:

```text
DiagnosticRecord
!= canonical business/system truth
```

SPEC-13 preserves this through:

```text
separate Logs/Diagnostics storage
no raw-log replay into canonical state
semantic diagnostic views instead of DB imports
```

### Observability & Diagnostics owner

The owner may:

- define event schema;
- record evidence;
- correlate events;
- generate diagnostic views/bundles;
- manage diagnostic retention.

It may not:

- decide entitlement;
- commit mode;
- commit installed release;
- declare external game installation truth;
- authorize Broker operation merely because a log says it is authorized.

---

## 4. SPEC-01 mapping

SPEC-01 process topology becomes diagnostic resource context:

```text
component
componentVersion
processInstanceId
windowsSessionId
releaseId
```

Runtime/Manager/Launcher/Broker keep separate process identities.

---

## 5. SPEC-02 mapping

Existing IPC fields remain canonical meanings:

```text
correlationId
operationId
requestId
idempotencyKey
fenceToken
```

SPEC-13 records/propagates them but does not redefine them.

Broker authorization audit uses OS-observed caller identity, not caller-supplied diagnostic fields.

---

## 6. SPEC-03 mapping

SPEC-03 reserved:

```text
%ProgramData%\SplitOS\Logs
%LocalAppData%\SplitOS\Logs
```

SPEC-13 fills those paths with bounded diagnostic policy.

Canonical SQLite remains separate.

---

## 7. SPEC-04 mapping

Account/auth/entitlement diagnostics record semantic outcomes only.

Forbidden diagnostics include:

```text
access token
refresh token
authorization code
PKCE verifier
password/cookie
```

Hosted checkout/payment evidence is not reproduced as secret/raw transaction material in logs.

---

## 8. SPEC-05 mapping

Mode transition timeline records:

```text
request
blockers
policy resolution
action apply
actual-state verification
commit / rollback / safe fallback
```

It preserves:

```text
source committed mode remains canonical until commit
```

and rollback terminal semantics (`CANCELLED` / `FAILED_WITH_SAFE_FALLBACK`).

---

## 9. SPEC-06 mapping

Windows integration diagnostics distinguish:

```text
desired target
resolved Windows target
apply technical result
actual read-back evidence
verification result
```

This preserves:

```text
API success != target state reached
```

Device snapshot generation/invalidation can be included for race diagnosis without making device IDs public by default.

---

## 10. SPEC-07 mapping

Game launch events preserve:

```text
HANDOFF_ACCEPTED
!= GAME_RUNNING
```

and record process proof/correlation class without exporting the full process inventory by default.

Client-specific evidence stays scoped to supported client capability status.

---

## 11. SPEC-08 mapping

Optimization diagnostics may record:

```text
profileId/pseudonym
resolved scenario
recommendation version
performance target class
user-lock conflict
apply result
representative telemetry summary
```

Raw high-frequency gameplay telemetry follows its own bounded capture/sampling policy.

---

## 12. SPEC-09 mapping

Launcher/Shared App diagnostics record semantic route/focus/presentation outcomes where relevant.

They do not log arbitrary user content from Shared App windows.

Window evidence uses bounded identity/bounds/status, not screenshots/content capture by default.

---

## 13. SPEC-10 mapping

Builder diagnostics are release/build artifacts outside normal installed runtime, but use compatible principles:

```text
operation
input identity
postcondition
result
BuildReceipt correlation
```

The installed support bundle may reference its BuildReceipt/baseline identifiers but does not need to reproduce Microsoft Windows binaries or full build logs.

---

## 14. SPEC-11 mapping

Update/recovery diagnostics use the durable identities already specified:

```text
UpdateTransactionId
RecoveryId
sourceRelease
targetRelease
commitDurable
RecoveryAuthorization
capsule verification
```

Reboot creates new process/trace context but does not lose durable transaction correlation.

WinRE Recovery Tool can export a bounded diagnostic package without generic filesystem access.

---

## 15. SPEC-12 mapping

Security/release trust audit covers:

```text
TUF metadata refresh/rollback/expiry result
Root transition result
artifact digest result
Authenticode publisher result
securityEpoch/releaseSequence denial
revocation state applied
RecoveryAuthorization allow/deny
```

No signing/private key material enters diagnostics.

---

## 16. SPEC decisions

```text
SPEC-DEC-146 local-first observability
SPEC-DEC-147 three observability planes
SPEC-DEC-148 versioned structured event envelope
SPEC-DEC-149 distinct correlation IDs preserved
SPEC-DEC-150 rotating NDJSON durable local sink
SPEC-DEC-151 ETW/TraceLogging detailed trace substrate
SPEC-DEC-152 protected machine security audit
SPEC-DEC-153 WER minidump default
SPEC-DEC-154 selective/redacted diagnostic bundle
SPEC-DEC-155 secret-forbidden fields / fail-closed export
SPEC-DEC-156 bounded retention/disk priority
SPEC-DEC-157 OpenTelemetry interoperability mapping only
```

---

## 17. Verification handoff

SPEC-14 acceptance families generated from SPEC-13:

```text
VA-OBS-EVENT-*
VA-OBS-CORR-*
VA-OBS-STORAGE-*
VA-OBS-AUDIT-*
VA-OBS-CRASH-*
VA-OBS-TRACE-*
VA-OBS-BUNDLE-*
VA-OBS-PRIVACY-*
VA-OBS-RETENTION-*
VA-OBS-PERF-*
```

Key acceptance chains:

```text
Requirement
→ event/audit/bundle contract
→ implementation provider/sink
→ reproducible verification case
```

---

## 18. Remaining OPEN items

Remain engineering/product gates rather than silent implementation choices:

- exact event provider GUID allocation;
- exact NDJSON size/rotation tuning;
- WER custom-minidump flags;
- automatic cloud telemetry requirements;
- support backend/upload retention;
- user-facing log viewer;
- audit hash-chain enhancement;
- production observability overhead budgets/SLOs.
