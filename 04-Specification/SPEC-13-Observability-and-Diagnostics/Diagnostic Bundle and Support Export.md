# SPEC-13 — Diagnostic Bundle and Support Export

## 1. Purpose

Defines how SplitOS assembles a bounded, explainable diagnostic package for user/support analysis without exporting the user's entire machine state.

---

## 2. Bundle goals

The bundle should answer:

```text
What failed?
When?
Which SplitOS release/components were involved?
Which operation/transaction was active?
What target was requested?
What actual-state evidence was observed?
What verification/failure occurred?
What recovery/fallback happened?
```

It is not intended to be:

- a full disk image;
- a full user profile archive;
- a raw copy of all SplitOS databases;
- a full process list history;
- an automatic cloud telemetry payload.

---

## 3. Bundle creation flow

```text
User selects Diagnostics / incident
↓
Runtime resolves incident/time window
↓
collect semantic summaries
↓
select correlated log/audit records
↓
optional artifacts selected
↓
redact / pseudonymize
↓
validate bundle policy
↓
create manifest + archive
↓
show summary
↓
Export locally
or explicit Send to Support when such backend exists
```

If redaction validation fails:

```text
EXPORT_BLOCKED_REDACTION_FAILED
```

No silent fallback to raw export.

---

## 4. Bundle identity

```text
bundleId
createdAtUtc
bundleSchemaVersion
SplitOS release
creator component/version
incident type
requested time range
correlation IDs / transaction IDs included
privacy mode
optional artifact list
bundle SHA-256 after finalization where applicable
```

---

## 5. Default structure

```text
SplitOS-Diagnostics-<timestamp>-<shortId>.zip
│
├── manifest.json
├── summary.json
│
├── events/
│   ├── user.ndjson
│   └── machine.ndjson
│
├── audit/
│   └── relevant-audit.ndjson
│
├── state/
│   ├── runtime-summary.json
│   ├── mode-summary.json
│   ├── game-session-summary.json
│   ├── update-summary.json
│   └── recovery-summary.json
│
├── environment/
│   ├── windows.json
│   ├── hardware-summary.json
│   ├── display-summary.json
│   └── integration-summary.json
│
├── compatibility/
│   └── decisions.json
│
├── traces/                    optional
└── crashes/                   optional
```

Absent/non-relevant files are omitted rather than emitted empty by requirement.

---

## 6. `manifest.json`

Contains machine-readable inventory of the bundle itself.

Each file entry includes:

```text
relativePath
kind
sizeBytes
sha256
privacyClass
sourceTimeRange
redactionProfile
```

Manifest does not make an included record authoritative.

---

## 7. `summary.json`

A normalized support summary, conceptually:

```json
{
  "incident": "MODE_TRANSITION_FAILURE",
  "outcome": "FAILED_SAFE_FALLBACK",
  "startedAtUtc": "...",
  "endedAtUtc": "...",
  "sourceState": "WORK",
  "targetState": "GAME",
  "canonicalStateAfter": "WORK",
  "failedComponent": "DisplayContext",
  "errorCode": "TARGET_STATE_NOT_REACHED",
  "recoveryResult": "ROLLBACK_SOURCE_VERIFIED"
}
```

The summary is derived diagnostic presentation.

Canonical owners remain the underlying state/transaction domains.

---

## 8. Incident selectors

Bundle can be built around:

```text
correlationId
modeTransitionId
gameLaunchId
updateTransactionId
recoveryId
crashArtifactId
explicit time range
```

Default time expansion SHOULD include bounded context before/after the incident, e.g. minutes rather than whole-day history, unless user chooses a broader capture.

Exact window values are UX/verification tunables.

---

## 9. Canonical state snapshots

The bundle does not copy raw `machine.db` / `user.db` by default.

Instead each semantic owner/gateway produces a bounded read-only diagnostic summary.

Examples:

```text
ModeStateDiagnosticView
UpdateTransactionDiagnosticView
RecoveryDiagnosticView
GameProfileDiagnosticView
```

This avoids exposing unrelated records/secrets and avoids interpreting raw DB schema in support tooling.

---

## 10. Transaction records

For a relevant durable transaction, include an explicit sanitized transaction summary:

```text
identity
source/target
state timeline
commitDurable
selected action journal
verification predicates
terminal outcome
```

Do not include arbitrary binary blobs or secrets referenced by the transaction.

---

## 11. Environment summary

Default environment summary may include:

```text
Windows edition/build/revision
SplitOS release/component versions
CPU model class
GPU model + driver version
RAM size class/amount
storage free-space evidence relevant to incident
display models/capabilities relevant to incident
controller/input class relevant to incident
Game Client version/capability status relevant to incident
```

It excludes serial numbers/full hardware identifiers unless explicitly required for a selected incident capture.

---

## 12. Game privacy

A game-launch incident bundle may include:

```text
SplitOS GameId
game title involved
external client type
external title identity (e.g. Steam AppID)
profile used
launch evidence
```

It does not include the user's whole game library by default.

---

## 13. Process evidence

If process evidence is relevant, include only bounded incident-related processes:

```text
expected game executable
launcher/bootstrap candidates
SplitOS process identities
```

Do not export a full historical inventory of all user processes unless the user explicitly enables an advanced capture that requires it.

---

## 14. File path treatment

Default bundle converts known roots:

```text
C:\Users\<user>\...
→ %USERPROFILE%\...

C:\ProgramData\SplitOS\...
→ %PROGRAMDATA%\SplitOS\...
```

Non-SplitOS arbitrary user paths are reduced to the minimum useful representation.

Example:

```text
D:\Projects\SecretClient\server.exe
```

may become:

```text
<user-path>\server.exe
```

when the full directory name is not required.

---

## 15. Optional ETL

Raw ETW trace inclusion is opt-in.

The bundle UI/manifest must identify:

```text
Detailed Windows/ETW trace included
```

because raw ETL can carry significantly more environmental information than sanitized structured events.

---

## 16. Optional crash dump

Crash dump inclusion is opt-in.

If selected:

- show size;
- show dump type;
- warn that process-memory data may be present;
- include only selected dump(s);
- record selection in bundle manifest.

---

## 17. Bundle staging

Bundle creation uses a SplitOS staging directory:

```text
%LocalAppData%\SplitOS\Diagnostics\BundleStaging
```

for normal user bundle generation.

Machine/recovery bundle creation may use protected `%ProgramData%` staging when normal user scope is unavailable.

Staging paths are not long-term storage.

---

## 18. Symlink/reparse safety

Bundle collector MUST NOT blindly follow reparse points/symlinks from diagnostic roots into arbitrary locations.

Collector uses known files produced by SplitOS and validates final paths remain inside allowed roots before reading/deleting them.

This is especially important for privileged recovery/support tools.

---

## 19. Local export

User chooses destination through normal UI.

The exported archive becomes user-owned.

SplitOS:

- does not silently delete it;
- does not silently upload it;
- does not treat it as a recovery image;
- does not import it back into product state.

---

## 20. Support upload

If a SplitOS support backend is later implemented, v1-compatible flow is:

```text
bundle created locally
↓
user sees summary/categories/size
↓
explicit Send to Support
↓
TLS authenticated upload
↓
server receipt ID
```

Exact backend, authentication, retention and jurisdiction/privacy requirements require their own requirements/security review.

SPEC-13 alone does not authorize background upload.

---

## 21. Bundle encryption

Local ZIP encryption is not fixed by v1 SPEC-13.

Future direct support upload should protect transport via authenticated TLS and may add end-to-end bundle encryption if threat/product requirements justify it.

Do not invent a custom encryption format casually.

---

## 22. Deterministic support metadata

Two bundles for the same retained incident should produce semantically equivalent summaries even if archive timestamps/compression bytes differ.

This supports reproducible support analysis.

---

## 23. Partial bundle

If a non-mandatory source is unavailable:

```text
bundleStatus = PARTIAL
missingSource[]
```

Example:

```text
ETW trace expired
crash dump rotated
```

The tool must not pretend the bundle is complete.

If mandatory redaction/manifest generation fails, creation/export fails instead.

---

## 24. Bundle size limit

The normal bundle has a configurable maximum.

Large optional artifacts are shown separately so a 3 GB dump does not silently turn a small support package into a huge upload.

When size would exceed the limit, user chooses which optional artifacts to exclude or export separately.

---

## 25. WinRE export

When normal SplitOS UI is unavailable, the bounded WinRE Recovery Tool may export a recovery diagnostic bundle to a user-selected removable/local destination.

Recovery bundle can include:

```text
UpdateTransaction
RecoveryContext
Release trust results
capsule verification
machine-level SplitOS audit/logs
```

It must not expose a generic filesystem browser merely to implement export.

---

## 26. Verification

SPEC-14 must cover:

- bundle from Mode failure;
- bundle from game launch failure;
- bundle from update/recovery incident;
- unrelated audit/game library omitted;
- user path redaction;
- secret scanning;
- optional ETL/dump not included by default;
- partial bundle behavior;
- malicious symlink/reparse point inside log root;
- WinRE bundle export;
- failed redaction blocks export.
