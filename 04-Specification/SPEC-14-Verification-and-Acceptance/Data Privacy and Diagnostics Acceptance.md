# SPEC-14 — Data, Privacy & Diagnostics Acceptance

## 1. Purpose

Defines verification for local canonical data integrity, migration, diagnostic evidence separation, privacy redaction and support export behavior.

Core distinctions:

```text
canonical data
!= projection/cache
!= diagnostics
!= verification evidence
```

and:

```text
local discovery
!= remote transmission
```

---

# 2. Canonical store integrity

## DATA-001 — Machine canonical writer boundary

Verify normal ordinary user processes cannot directly write machine canonical state.

Expected writer path remains Broker/persistence boundary.

## DATA-002 — User canonical writer boundary

Manager/GameLauncher do not directly own/write `user.db`; semantic changes flow through Runtime Host owners.

## DATA-003 — Cache is not truth

Delete/rebuild projection cache and verify:

```text
Game Profiles preserved
account association preserved
mode/release state preserved
external library reconciles again
```

## DATA-004 — Diagnostics deletion is safe

Delete old local diagnostic records according to policy.

Expected no canonical product state changes.

---

# 3. Durability verification

For machine/user canonical SQLite stores:

- verify configured durability profile matches SPEC-03;
- exercise graceful and abrupt process termination around writes;
- verify transaction atomicity;
- verify WAL-aware backup behavior;
- verify no backup workflow copies only main DB while leaving required live WAL state behind;
- verify required durable write failure prevents semantic success report.

---

# 4. Migration fixture library

Every schema migration requires versioned fixtures containing representative historical data.

At minimum user fixtures should include where relevant:

```text
multiple Windows users
SplitOS account association
Desktop/TV Game Profiles
Game Profile overrides/locks
Shared Apps
preferences
old/unknown optional fields
external-drift reconciliation metadata
```

Machine fixtures include:

```text
installed release identity
security floors
transition/update/recovery records
mutation lease state
accepted policy/catalog references
```

---

# 5. Forward/backward rollback preservation

Because SPEC-11 requires one previous-release software rollback without user-data rollback, verify:

```text
N user data
→ migrate/use under N+1
→ modify under N+1
→ rollback software to N
→ N can read/preserve required current data or bridge transforms it safely
```

A field unknown to N but required to survive must not be silently dropped if the compatibility contract says preserve.

---

# 6. Corruption handling

## DATA-020 — Projection corruption

May be discarded/rebuilt.

## DATA-021 — User canonical corruption

Must surface recovery/backup handling; no silent reset presented as success.

## DATA-022 — Machine canonical corruption

Triggers protected recovery/repair path. Diagnostic logs cannot become automatic reconstructed truth.

## DATA-023 — Backup integrity

Backup selection requires schema/integrity validation; filename timestamp alone is insufficient proof.

---

# 7. Secret storage acceptance

Verify reusable account credentials:

```text
not stored in plaintext SQLite
protected through SPEC-04 secret store
not included in ordinary DB backup/export
not emitted to logs
```

Synthetic known secret fixtures should be scanned across:

```text
user.db fields expected non-secret
logs
audit
ETL metadata where parsable
support bundle
crash policy metadata
```

Crash memory content is treated separately as potentially sensitive artifact.

---

# 8. Diagnostic event contract acceptance

For representative flows verify events are:

```text
typed
versioned
structured
correlated
privacy-classified
```

Free-form message text cannot be the only machine-readable indication of mandatory outcome.

Representative events:

```text
Mode transition
Windows target apply/read-back
Game launch handoff/running/exit
Update state
Recovery action
Broker authorization
release trust verification
```

---

# 9. Diagnostic truth-separation test

Create deliberate inconsistency fixture:

```text
old log says GAME committed
canonical state says WORK
```

Restart/reconcile system.

Expected: canonical owner/durable transaction semantics win; diagnostic event triggers investigation only.

Equivalent fixtures should exist for entitlement/update/release where practical.

---

# 10. Correlation acceptance

For each mandatory timeline family, verify support tooling can reconstruct events using correct distinct IDs:

```text
correlationId
operationId
transactionId
requestId
traceId/spanId
processInstanceId
```

Required timelines:

```text
Mode transition
Game launch/session
Update/reboot/resume
Recovery
Broker privileged operation
Release trust validation
```

A reboot may change trace/process IDs but must preserve durable transaction/correlation identity where specified.

---

# 11. Security audit acceptance

For protected actions verify audit records contain enough evidence to answer:

```text
who/which process requested?
which actual Windows session?
which capability?
which target ID?
which fence token/owner context?
allow or deny?
which technical result?
```

Audit must not include reusable secrets.

For capabilities classified `REQUIRED_BEFORE_ACTION`, force audit sink failure and verify sensitive action does not proceed if contract requires fail-closed audit availability.

---

# 12. Crash artifact acceptance

Default WER LocalDumps behavior:

```text
minidump
bounded count
local only
bounded retention
```

Test crash of at least:

```text
RuntimeHost
Broker
Manager
GameLauncher
UpdateBootstrap / Recovery tool where mechanism supports
```

Verify default crash handling does not automatically upload dumps.

Full dumps require explicit deep-diagnostics mode and warning.

---

# 13. ETW/deep trace acceptance

Verify:

```text
normal mode → heavy capture off
user/support starts bounded capture
providers enable without requiring product restart where designed
capture stops
artifact stored under diagnostic boundary
retention cleanup applies
```

Deep trace must not become required for normal correctness.

---

# 14. Diagnostic bundle acceptance

## DIAG-001 — Incident-scoped bundle

Generate from a known correlation/transaction ID.

Expected bundle contains relevant bounded timeline/state/environment evidence.

## DIAG-002 — No raw canonical DB by default

Standard bundle does not copy unrestricted `machine.db`/`user.db`.

## DIAG-003 — No whole library/process history by default

Only incident-relevant bounded evidence.

## DIAG-004 — Optional artifacts explicit

ETL/dump inclusion requires explicit selection and is visible in manifest/summary.

## DIAG-005 — Bundle self-description

Manifest declares:

```text
bundle format version
creation time
candidate/release identity
incident selector
included categories
redaction status
optional sensitive-artifact flags
```

---

# 15. Privacy classification acceptance

Fields use:

```text
PUBLIC_PRODUCT
PSEUDONYMOUS_DIAGNOSTIC
USER_ENVIRONMENT
SENSITIVE
SECRET_FORBIDDEN
```

Test schemas/fixtures so prohibited values cannot silently enter ordinary diagnostics.

---

# 16. Path redaction acceptance

Use fixtures containing:

```text
C:\Users\Alice\...
project/customer folder names
personal document names
```

Default export must normalize/redact when exact path is unnecessary for incident diagnosis.

If exact path is explicitly required for a selected diagnostic category, it must be classified accordingly and surfaced in export sensitivity summary.

---

# 17. Pseudonym acceptance

Bundle-scoped pseudonyms for account/installation/device IDs should:

```text
be stable within one bundle where correlation needs it
not reveal raw source ID
not necessarily be stable across unrelated bundles unless product policy requires
```

---

# 18. Secret redaction fail-closed

Seed synthetic values matching:

```text
OAuth access token
refresh token
Authorization header
cookie
PKCE verifier
authorization code
private key fixture
payment-card-like secret fixture where scanner supports
```

If export validator detects forbidden secret after redaction stage:

```text
EXPORT_BLOCKED_REDACTION_FAILED
```

There is no automatic raw fallback.

---

# 19. No implicit remote telemetry

Network observation test under default v1 configuration.

Expected:

- account/update product traffic occurs only for their declared functions;
- local hardware/game/process diagnostics are not transmitted merely because collected;
- crash dumps not uploaded automatically;
- support bundle transmitted only after explicit supported user action if upload feature exists.

If future product requirements add telemetry, this test/profile must be revised through requirements/spec chain.

---

# 20. Retention acceptance

Validate initial SPEC-13 defaults or release-profile values:

```text
Operational logs → 14 days OR 256 MiB per scope
Security audit   → 30 days OR 128 MiB
ETW              → 24 hours unless exported
MiniDumps         → max 5/process, normally 7 days
Bundle staging    → removed after export or within 24h
```

Where these values are changed by validated release policy, exact profile values become acceptance targets.

---

# 21. Disk-pressure ordering

Under constrained disk space, verify cleanup priority:

```text
canonical state
> recovery safety
> protected audit
> operational logs
> verbose traces
```

Diagnostics must not delete required Recovery Capsule/canonical DB merely to preserve verbose trace.

---

# 22. Supportability acceptance

A representative support engineer using only exported bundle should be able to answer for selected incidents:

```text
what user action occurred?
what target was intended?
what canonical state existed before/after?
what actual Windows/client evidence was observed?
what failed?
what recovery happened?
what release/hardware/compatibility context applied?
```

This can be validated through blind incident-review exercises during release qualification.

---

# 23. Result

GATE-11 passes only when SplitOS is diagnosable enough to support failures while preserving the stronger rule that diagnostics are bounded evidence, secrets stay protected, and local observation does not become hidden remote telemetry.
