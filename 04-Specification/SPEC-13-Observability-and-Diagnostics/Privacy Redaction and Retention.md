# SPEC-13 — Privacy, Redaction and Retention

## 1. Purpose

Defines data minimization, privacy classes, redaction, retention and deletion semantics for SplitOS diagnostics.

This contract implements the existing principle:

```text
local discovery
!= automatic external transmission
```

---

## 2. Privacy classes

Every diagnostic field/artifact is classified.

### `PUBLIC_PRODUCT`

Non-user-specific product facts.

Examples:

```text
SplitOS component version
event name
error code
release ID
Windows compatibility status class
```

### `PSEUDONYMOUS_DIAGNOSTIC`

Stable enough for incident correlation but not intended to expose direct user identity.

Examples:

```text
bundle-scoped account pseudonym
installation pseudonym
processInstanceId
correlationId
```

### `USER_ENVIRONMENT`

Information about the user's machine/app environment.

Examples:

```text
GPU model
display model
game title
external client version
selected app/process name
redacted paths
```

### `SENSITIVE`

Potentially sensitive diagnostic material requiring explicit handling.

Examples:

```text
raw ETW trace
process memory dump
full local file path
external account identity
machine/device serial-like identifiers
```

### `SECRET_FORBIDDEN`

Must not be written to diagnostic sinks.

---

## 3. Secret-forbidden list

At minimum:

```text
passwords
OAuth access tokens
refresh tokens
authorization codes
PKCE verifier
session cookies
Authorization headers
API secrets
payment card data
private signing keys
HSM credentials
recovery/signing private material
raw DPAPI-protected secret plaintext
```

If a secret is accidentally passed to the diagnostic API, the library should reject/redact according to field contract and emit a safe diagnostic health signal without repeating the secret.

---

## 4. Schema-first privacy

Privacy class belongs to the event schema/field definition.

Avoid:

```text
write arbitrary object
→ try to regex-redact later
```

Preferred:

```text
known typed field
→ privacy classification
→ sink/export policy
```

Regex/text scanning remains a defense-in-depth export check, not the primary privacy design.

---

## 5. User identity

Operational logs generally do not need user name/email.

Prefer:

```text
windowsSessionId
userContext = CURRENT_CONSOLE_USER
bundleScopedUserId
```

rather than:

```text
DOMAIN\alice
alice@example.com
```

Exact identity may be shown locally in account UI without being duplicated into diagnostics.

---

## 6. Account identifiers

Local protected logs may retain a stable internal association identifier if necessary for correctness investigation.

Default exported bundle converts it into a bundle-scoped pseudonym unless exact identity is explicitly needed for a support workflow with appropriate consent.

Entitlement value may be represented semantically:

```text
FREE
PRO
ENTITLEMENT_UNAVAILABLE
```

without logging the token/assertion itself.

---

## 7. File paths

### SplitOS-known roots

Normalize:

```text
%PROGRAMDATA%
%LOCALAPPDATA%
%USERPROFILE%
%PROGRAMFILES%
```

### Arbitrary user paths

Default export minimizes parent directory disclosure.

Keep only diagnostically required suffix/role.

Example:

```text
C:\Users\Alice\Clients\SecretProject\build\server.exe
```

may export as:

```text
%USERPROFILE%\<redacted>\server.exe
```

when parent names are not required.

---

## 8. URLs and network data

Default logs record structured network metadata such as:

```text
service = AccountBackend
operation = EntitlementRefresh
HTTP status class
latency
retry count
```

Avoid full arbitrary URL/query/header capture.

Allowed URL fields are allowlisted.

Never log:

```text
Authorization
Cookie
Set-Cookie
OAuth code query parameter
access token query parameter
```

---

## 9. Game/library privacy

Local SplitOS may discover games for product behavior.

Diagnostics should record only incident-relevant game identities by default.

Do not export the complete installed game list for a display/update/Broker problem.

A dedicated game-library diagnostic action may include broader inventory only after the user explicitly selects that diagnostic scope.

---

## 10. Process privacy

Normal product diagnostics do not continuously retain all process command lines.

Process evidence should use:

```text
image identity/path class
PID + creation time
session
classification
correlation role
```

Command-line capture is `SENSITIVE` and allowed only when a named integration diagnostic requires it and redaction rules are defined.

---

## 11. Hardware privacy

Useful default fields:

```text
CPU model
GPU model
driver version
RAM amount
Windows build
display model/capabilities
input-device class/model where relevant
```

Omit by default:

```text
disk serial
machine serial
MAC address
full PnP instance path when not required
controller serial-like stable IDs
```

Where device matching requires a stable ID internally, export a bundle-scoped pseudonym unless exact value is needed.

---

## 12. Redaction pipeline

```text
collect typed diagnostic data
↓
apply field privacy policy
↓
normalize known paths/IDs
↓
remove forbidden fields
↓
defense-in-depth secret scan
↓
validate manifest privacy class
↓
allow local export/upload
```

Any stage can fail closed for external export.

---

## 13. Redaction profiles

Candidate profiles:

```text
LOCAL_VIEW
DEFAULT_EXPORT
DEEP_SUPPORT_EXPORT
WINRE_RECOVERY_EXPORT
```

`DEEP_SUPPORT_EXPORT` may include more `SENSITIVE` artifacts but still never includes `SECRET_FORBIDDEN` fields intentionally.

---

## 14. Local viewing vs export

A local admin/user may legitimately see more environmental detail than a default exported bundle.

Therefore:

```text
local log storage policy
!= export policy
```

Export always passes through a separate redaction/selection pipeline.

---

## 15. Retention defaults

Initial v1 policy:

| Data | Default retention |
|---|---|
| User operational events | 14 days or 256 MiB |
| Machine operational events | 14 days or 256 MiB |
| Protected security audit | 30 days or 128 MiB |
| On-demand ETW trace | 24 hours unless selected/exported |
| Crash minidump | max 5/process, normally 7 days |
| Full deep-diagnostic dump | shortest practical support window; explicit cleanup notice |
| Bundle staging | delete after export or 24 hours |
| User-exported bundle | user-owned; no automatic SplitOS deletion |

Time and size limits are combined: whichever requires rotation first applies unless incident pinning is active.

---

## 16. Incident pinning

A user may explicitly select an incident for diagnostic export.

The minimum relevant local artifacts may be temporarily pinned during bundle creation.

Pinning:

- is bounded in time;
- does not defeat disk safety indefinitely;
- cannot pin arbitrary filesystem content;
- is removed after export/cancel timeout.

---

## 17. Disk pressure order

Eviction priority:

```text
expired verbose ETW traces
↓
expired bundle staging
↓
expired crash artifacts
↓
ordinary operational log rotation
↓
security audit rotation by policy
```

Never automatically delete:

```text
machine.db
user.db
required Recovery Capsule
installed release trust metadata
```

just to retain diagnostics.

---

## 18. User cleanup

Manager MAY expose:

```text
Clear diagnostic logs
Clear crash dumps
Clear support captures
```

with scope/size shown.

This must not delete canonical state or current recovery capsule.

Protected security audit cleanup may require elevated/administrative policy and should itself be auditable where practical.

---

## 19. Sign-out/account deletion

Diagnostic retention is not automatically identical to account-data deletion semantics.

If product policy later requires account erasure/remote privacy workflows, requirements must define which local diagnostic records must be removed/anonymized and which security/recovery records can legally remain.

SPEC-13 does not silently decide regulatory retention policy.

---

## 20. Remote support retention

No server-side support retention period is defined by SPEC-13 because continuous/support backend telemetry has not yet been introduced at requirements level.

When implemented, server-side retention must be explicit and user-visible where required.

---

## 21. Crash dump sensitivity

Minidump is still treated as `SENSITIVE` even if smaller than a full dump.

Full dump receives strongest warning because it may contain broad process memory.

Dumps are excluded from normal automatic export.

---

## 22. ETL sensitivity

ETL sensitivity depends on providers enabled.

A capture profile has a privacy rating:

```text
STANDARD
ELEVATED
SENSITIVE
```

The UI can surface this rating before capture/export.

---

## 23. Redaction failure semantics

If an event field marked `SECRET_FORBIDDEN` is found during bundle validation:

```text
quarantine affected generated bundle
→ do not upload/export raw archive
→ record safe redaction failure
→ user may retry after tooling repair
```

Do not display the detected secret value in the error message.

---

## 24. Verification

SPEC-14 must include automated secret-canary tests:

```text
fake access token
fake refresh token
fake password
fake Authorization header
fake user path/account email
```

and prove expected:

- forbidden values absent from normal logs;
- default bundle pseudonymizes/redacts required fields;
- raw optional dump/ETL only appears after selection;
- retention removes eligible data and preserves canonical/recovery stores;
- redaction failure blocks export.
