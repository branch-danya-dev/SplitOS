# SPEC-04 — Token and Secret Storage

## 1. Purpose

This document defines which authentication/entitlement artifacts may exist on the desktop, their lifetime, and how reusable secrets are protected.

Core rule:

```text
reusable secret
→ Runtime Host only
→ user-scoped Windows protection
→ never ordinary plaintext persistence
```

---

## 2. Secret classes

### 2.1 Access token

Classification:

```text
SHORT_LIVED_SECRET
```

Baseline:

- bearer token;
- nominal 15-minute lifetime;
- held in Runtime Host memory where practical;
- never written to logs;
- never exposed to Manager/Game Launcher;
- never used as long-term entitlement persistence.

If Runtime Host restarts and no access token remains, it may use a protected refresh token to obtain a new one.

---

### 2.2 Refresh token

Classification:

```text
REUSABLE_SECRET
```

Baseline server policy:

```text
rotation: every successful refresh
inactivity lifetime: <= 30 days
absolute lifetime: <= 90 days
```

Server MAY shorten either window.

The desktop MUST treat refresh-token family replay/reuse as a security event that forces reauthentication when signaled by the server.

---

### 2.3 Authorization code

Classification:

```text
TRANSIENT_ONE_TIME_SECRET
```

Rules:

- memory only;
- single-use;
- never logged;
- destroyed after exchange/terminal failure.

---

### 2.4 PKCE verifier / state / nonce

Classification:

```text
TRANSIENT_AUTH_SECRET_OR_CORRELATOR
```

Rules:

- memory only;
- one auth transaction only;
- not persisted to SQLite;
- not included in diagnostics.

---

### 2.5 Offline entitlement assertion

Classification:

```text
SIGNED_AUTHORIZATION_EVIDENCE
```

It is not a password/refresh token, but SHOULD still be stored inside the user-scoped DPAPI blob to reduce trivial copying across user/machine contexts.

---

## 3. v1 protected storage mechanism

v1 uses Windows DPAPI:

```text
CryptProtectData
CryptUnprotectData
```

without `CRYPTPROTECT_LOCAL_MACHINE`.

Therefore protection is scoped to the current Windows user credentials/context and normally the same machine.

SplitOS MUST NOT use machine-scope DPAPI for normal user refresh tokens because that would allow other local users/process identities with access to the blob to decrypt under machine scope.

---

## 4. Protected file location

Recommended path:

```text
%LocalAppData%\SplitOS\Secrets\account.v1.dat
```

The file is an opaque DPAPI ciphertext container.

Directory/file ACL baseline:

```text
current Windows user: read/write
SYSTEM: read only if required by install/support policy; default no application read requirement
Administrators: inherited OS ownership/backup semantics only; not application auth path
other users: no access
```

Runtime Host is the only normal product process that may decrypt/use the blob.

Manager and Game Launcher MUST access auth state through Runtime Host semantic IPC, never by opening this file.

---

## 5. Plaintext structure before DPAPI protection

Logical v1 secret payload:

```json
{
  "formatVersion": 1,
  "accountId": "acc_...",
  "refreshToken": "opaque-secret",
  "refreshTokenFamilyId": "optional-reference",
  "refreshIssuedUtc": "...",
  "refreshAbsoluteExpiryUtc": "...",
  "lastTrustedServerUtc": "...",
  "offlineEntitlementAssertion": "optional-jws",
  "offlineAssertionStoredUtc": "..."
}
```

The actual serialized format MAY be JSON or a compact binary envelope; changing this internal plaintext representation does not change the trust model.

The DPAPI-protected file MUST include a format/version marker outside or inside the encrypted envelope sufficient to reject unsupported formats safely.

---

## 6. Optional DPAPI entropy

Runtime MAY supply stable application-specific optional entropy to DPAPI.

If used:

- it MUST be release/runtime-owned, not user-provided;
- loss of entropy MUST be treated like loss of secret material;
- it MUST NOT be mistaken for a cryptographic key that allows cross-machine recovery.

v1 may omit optional entropy if it complicates safe upgrade/recovery.

---

## 7. Secret read lifecycle

At Runtime Host startup:

```text
locate DPAPI blob
→ CryptUnprotectData
→ validate internal format
→ validate account association match
→ load refresh token into protected process memory
→ do not expose plaintext outside auth module
```

If decryption fails:

```text
LOCAL_SECRET_UNREADABLE
→ do not guess/reconstruct token
→ clear unusable session reference
→ mark REAUTH_REQUIRED
→ preserve ordinary Windows usability
```

DPAPI failure may occur after certain credential/reset/profile-recovery scenarios; reauthentication is acceptable product behavior.

---

## 8. Secret write lifecycle

When a new refresh token is issued:

```text
build new plaintext secret envelope
→ CryptProtectData
→ write new ciphertext to temporary file
→ durable replace existing account.v1.dat
→ only then retire previous local token state
```

The update MUST avoid a window where an app crash leaves a truncated plaintext/ciphertext file.

Recommended filesystem pattern:

```text
write account.v1.new
flush/close
atomic replace/rename
```

No plaintext temporary file is permitted.

---

## 9. Refresh-token rotation

Refresh flow:

```text
current refresh token R1
→ token endpoint
→ receive access token A2 + refresh token R2
→ protect/store R2 successfully
→ switch in-memory active refresh token to R2
→ R1 becomes retired
```

If the server rotates the token but local storage of R2 fails, Runtime Host MUST NOT repeatedly replay R1 indefinitely.

Required result:

```text
TOKEN_ROTATED_BUT_LOCAL_PERSIST_FAILED
→ current server session may be uncertain
→ require reauthentication / controlled recovery
```

This avoids creating an accidental replay loop.

---

## 10. Refresh-token replay signal

If backend reports reuse/replay/invalidated token family:

```text
clear access token
clear protected refresh token
clear offline assertion if policy requires
mark REAUTH_REQUIRED
surface security/session-expired UX
```

Do not silently downgrade the error to generic network retry.

---

## 11. Sign-out cleanup

Explicit SplitOS sign-out MUST:

```text
best-effort revoke refresh/session server-side
→ clear access token memory
→ securely remove DPAPI blob
→ clear account association
→ clear offline authorization evidence
```

Deleting ciphertext is sufficient product behavior; SplitOS does not promise forensic secure erase on modern SSD/filesystems.

---

## 12. Backup / migration behavior

The DPAPI secret blob SHOULD NOT be included as a portable user-profile backup artifact intended for restore onto another machine.

If copied to another machine/user and decryption fails:

```text
REAUTH_REQUIRED
```

No product recovery path may export the refresh token in plaintext to make migration easier.

Game Profiles/preferences may be backed up separately without including credentials.

---

## 13. Diagnostics rules

Forbidden diagnostic fields:

```text
Authorization header
access token
refresh token
authorization code
PKCE verifier
raw DPAPI plaintext
full callback query string if it includes code/state
```

Allowed redacted metadata:

```text
accountId hash/reference
token refresh outcome
refresh family replay detected: true/false
auth transaction ID
HTTP status/error category
entitlement version
```

---

## 14. Process-memory handling

Runtime Host SHOULD minimize copies of reusable tokens.

Requirements:

- no token in exception messages;
- no token in telemetry;
- no token in UI IPC payloads;
- clear/dispose buffers where practical;
- use dedicated auth module boundary;
- never share refresh token with Game Client adapters.

This specification does not claim perfect memory secrecy against a hostile local Administrator/debugger, which is outside the v1 guarantee.

---

## 15. Acceptance criteria

1. refresh token is absent from user.db/projection.db/plain config;
2. only Runtime Host can normally decrypt/use reusable auth material;
3. DPAPI is user-scoped, not machine-scoped;
4. access token is not durable entitlement state;
5. token rotation replaces the protected token safely;
6. refresh-token reuse forces reauth/security handling;
7. DPAPI failure cannot fabricate a session;
8. sign-out removes local reusable auth/offline proof;
9. credentials are not exported in portable backup/support bundle;
10. logs contain no reusable/auth one-time secrets.

---

## 16. Engineering evidence

- `CryptProtectData`: https://learn.microsoft.com/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata
- `CryptUnprotectData`: https://learn.microsoft.com/windows/win32/api/dpapi/nf-dpapi-cryptunprotectdata
- OAuth refresh-token security / rotation: https://www.rfc-editor.org/rfc/rfc9700
