# SPEC-04 — Native Authentication Flow

## 1. Purpose

This document defines the v1 interactive SplitOS Account sign-in flow for Windows desktop clients.

The flow is designed for a public native client and MUST avoid embedding reusable client secrets or collecting passwords inside SplitOS desktop UI.

---

## 2. Client role

`SplitOS.RuntimeHost.exe` owns the auth transaction.

`SplitOS.Manager.exe` MAY initiate/present it but MUST NOT own reusable credentials.

```text
Manager
→ Auth.Start command
→ Runtime Host
→ system browser / local loopback receiver
→ Authorization Server
```

Game Launcher SHOULD NOT expose full sign-in implementation directly; it may request Manager/auth UX when required.

---

## 3. Protocol baseline

v1 uses:

```text
OAuth 2.0 Authorization Code
+
OpenID Connect identity layer
+
PKCE S256
+
external system browser
+
loopback IP redirect
```

Forbidden:

```text
Implicit Grant
Resource Owner Password Credentials
embedded desktop client secret
password collection in SplitOS Manager
PKCE plain method
```

---

## 4. Redirect strategy

v1 native redirect URI pattern:

```text
http://127.0.0.1:<ephemeral-port>/oauth/callback
```

Rules:

1. use IP literal `127.0.0.1`, not `localhost`;
2. bind listener only to loopback;
3. select an OS-assigned/free ephemeral port per transaction;
4. open listener immediately before browser authorization begins;
5. close listener after one terminal callback or auth timeout;
6. reject non-loopback requests;
7. path MUST be the registered `/oauth/callback` path;
8. authorization server permits port variation only for the registered loopback redirect pattern.

No local TLS certificate is required for loopback HTTP because traffic never leaves the host; this exception is specific to native loopback redirects.

---

## 5. Auth transaction record

Runtime Host creates an in-memory auth transaction:

```text
authTransactionId
windowsSessionId
windowsUserSidHash/reference
createdUtc
expiresUtc
redirectUri
state
nonce
codeVerifier
codeChallenge
codeChallengeMethod = S256
requestedScopes
status
```

### 5.1 Lifetime

v1 auth transaction lifetime:

```text
10 minutes maximum
```

After expiration:

```text
listener closed
state/codeVerifier discarded
authorization result rejected
```

### 5.2 Persistence

`state`, `nonce`, authorization code and PKCE verifier SHOULD remain memory-only.

They MUST NOT be written to ordinary logs or durable SQLite state.

A Runtime Host crash cancels the current interactive auth transaction; user may restart sign-in.

---

## 6. PKCE requirements

Runtime Host MUST generate a cryptographically random verifier.

Baseline:

```text
32 random bytes
→ base64url without padding
→ 43-character verifier
```

Then:

```text
codeChallenge = BASE64URL(SHA256(ASCII(codeVerifier)))
codeChallengeMethod = S256
```

Runtime Host MUST NOT downgrade to `plain` if S256 fails.

---

## 7. Authorization request

Representative request semantics:

```text
response_type=code
client_id=<public-native-client-id>
redirect_uri=http://127.0.0.1:<port>/oauth/callback
scope=openid profile email <SplitOS API scopes>
state=<random>
nonce=<random>
code_challenge=<S256 challenge>
code_challenge_method=S256
```

Only minimum scopes required by the desktop product SHOULD be requested.

The browser MUST be launched using Windows default system browser behavior.

---

## 8. Callback validation

Upon receiving loopback request Runtime Host MUST validate before token exchange:

```text
current auth transaction exists
request received on exact active listener
callback path matches
auth transaction not expired
state exact match
single-use callback not already consumed
```

If `error` is returned by authorization server, map it to controlled product outcome and destroy transaction.

Examples:

```text
access_denied → AUTH_CANCELLED
login_required → AUTH_LOGIN_REQUIRED
server_error → AUTH_SERVER_ERROR
```

Unknown/malformed callback parameters MUST be rejected.

---

## 9. Token exchange

Runtime Host sends authorization code directly to token endpoint over HTTPS.

Required token request semantics:

```text
grant_type=authorization_code
code=<one-time authorization code>
redirect_uri=<exact redirect used>
client_id=<public native client ID>
code_verifier=<original PKCE verifier>
```

No desktop client secret is sent.

Authorization code MUST be single-use.

Token exchange failure leaves existing account association unchanged unless this was initial association.

---

## 10. OIDC identity validation

When ID token is returned, Runtime Host MUST validate:

```text
issuer
audience/client_id
signature/key trust
expiration/not-before
nonce
subject existence
```

The canonical local account reference becomes the backend stable account identity (`sub`/mapped accountId), not email.

Example:

```text
sub = acc_01H...
email = user@example.com

canonical identity → sub/accountId
display field       → email
```

If OIDC token validation fails, no new account association is committed.

---

## 11. Account bootstrap after token exchange

After successful auth token validation:

```text
GET current account
→ obtain canonical accountId/profile status
GET current entitlement
→ obtain entitlementVersion/capabilities
protect refresh token locally
persist Windows user association metadata
recompute ManagedRuntimeAccessDecision
```

Recommended ordering for initial association:

```text
1. validate tokens/identity
2. retrieve account + entitlement
3. persist protected reusable credentials
4. persist account association
5. expose ASSOCIATED state
```

If durable credential/association persistence fails, the transaction MUST NOT report successful persistent login merely because the server token exchange succeeded.

---

## 12. Existing-account replacement

If current Windows user is already associated with Account A and user signs into Account B:

Runtime Host MUST treat this as an explicit account-switch operation.

It MUST NOT silently overwrite credentials.

Flow:

```text
Account A associated
→ user chooses Switch account
→ converge premium runtime as required
→ revoke/clear Account A local session
→ authenticate Account B
→ persist Account B association
→ resolve entitlement
```

Local Game Profiles/preferences are not automatically deleted unless product UI explicitly offers that action.

---

## 13. Concurrent auth attempts

Only one auth transaction SHOULD be active per Runtime Host/Windows session.

Second request:

```text
existing active transaction
→ return AUTH_ALREADY_IN_PROGRESS
or activate existing Manager/browser flow
```

Do not create multiple loopback listeners with competing account-switch intent.

---

## 14. Browser process is not trusted authority

Runtime Host MUST assume browser-visible/callback data is untrusted until protocol validation completes.

Specifically:

```text
browser says success
!= authenticated account

callback contains code
!= code accepted

checkout page says paid
!= PRO entitlement
```

Only validated authorization/token/backend responses can advance identity/entitlement state.

---

## 15. Failure / cleanup matrix

| Event | Cleanup | Product result |
|---|---|---|
| user closes browser | listener times out | AUTH_CANCELLED/TIMEOUT |
| state mismatch | destroy transaction | AUTH_RESULT_REJECTED |
| nonce mismatch | discard token result | AUTH_RESULT_REJECTED |
| token endpoint unreachable | destroy one-time local auth transaction | AUTH_BACKEND_UNAVAILABLE |
| code already used | reject | AUTH_CODE_INVALID |
| DPAPI protect fails | do not persist association as durable signed-in session | LOCAL_SECRET_STORE_FAILED |
| account API disabled account | clear new session material | ACCOUNT_DISABLED |
| entitlement endpoint unavailable after login | account may be associated; premium decision uses valid prior/offline evidence only | DEGRADED |

---

## 16. Acceptance criteria

1. login opens external system browser;
2. listener binds only to 127.0.0.1;
3. PKCE S256 is mandatory;
4. `state` and OIDC `nonce` are independently validated;
5. implicit/password grants do not exist;
6. no client secret is shipped in desktop binaries/config;
7. auth transaction secrets are not persisted/logged;
8. stable account ID, not email, becomes association key;
9. local persistence failure cannot be reported as durable login success;
10. browser/callback content cannot directly grant entitlement.

---

## 17. Engineering evidence

- RFC 8252 native applications: https://www.rfc-editor.org/rfc/rfc8252
- RFC 7636 PKCE: https://www.rfc-editor.org/rfc/rfc7636
- RFC 9700 OAuth security BCP: https://www.rfc-editor.org/rfc/rfc9700
- OIDC Core: https://openid.net/specs/openid-connect-core-1_0.html
