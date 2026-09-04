# SPEC-04 — Account / Auth / Entitlement Specification

Status: **READY FOR REVIEW**  
Scope: SplitOS Account identity, native authentication, Windows-user association, token lifecycle, FREE/PRO entitlement, offline premium evidence and subscription activation.  
Out of scope: backend implementation framework, payment-provider internals, exact signing-key hierarchy (`SPEC-12`), detailed Mode Runtime convergence (`SPEC-05`).

---

## 1. Purpose

This specification makes the product identity model implementable while preserving the architecture invariant:

```text
Windows identity
!= SplitOS Account
!= payment transaction
!= entitlement
!= managed runtime access decision
```

A SplitOS Account is the product identity associated with a Windows user context. Paid entitlement unlocks managed runtime capabilities, but failure to authenticate or prove PRO MUST NOT make base Windows unusable.

---

## 2. Canonical identity model

### 2.1 Windows identity

Windows remains authoritative for:

```text
Windows user SID
logon/session identity
interactive session
physical console ownership
```

SplitOS MUST NOT replace Windows authentication or become a Windows logon provider in v1.

### 2.2 SplitOS Account

SplitOS Backend is authoritative for:

```text
accountId
account lifecycle/account status
subscription/entitlement relationship
server-side session/revocation state
```

`accountId` MUST be a stable opaque server identifier.

Email, display name or other mutable profile fields MUST NOT be used as the canonical account key.

### 2.3 Windows user association

For one Windows user profile:

```text
Windows user
→ 0..1 active SplitOS Account association
```

A SplitOS Account MAY be associated with multiple installations/Windows users subject to backend product policy.

The exact numeric device/subscription limit is product policy and is not invented by this specification.

### 2.4 Account is required; payment is not

A configured SplitOS installation expects each Windows user who uses SplitOS product surfaces to establish a SplitOS Account association.

However:

```text
no SplitOS Account yet
or
backend unavailable
or
FREE entitlement
```

MUST still leave ordinary Windows desktop/base OS usable.

---

## 3. Runtime access outcomes

`Product Identity & Entitlement` owns `ManagedRuntimeAccessDecision`.

The decision MUST be derived from current evidence; it MUST NOT be a locally editable plan flag.

Canonical semantic outcomes remain compatible with A&D:

```text
ENABLED
DISABLED
DEGRADED
REAUTH_REQUIRED
```

Recommended reason codes include:

```text
FREE_ENTITLEMENT
PRO_ONLINE_CONFIRMED
PRO_OFFLINE_ASSERTION_VALID
ACCOUNT_NOT_ASSOCIATED
AUTH_SESSION_EXPIRED
REFRESH_TOKEN_REVOKED
ENTITLEMENT_EXPIRED
ENTITLEMENT_NOT_PROVEN_OFFLINE
BACKEND_UNAVAILABLE
CLOCK_ROLLBACK_SUSPECTED
ACCOUNT_DISABLED
```

### 3.1 FREE

```text
valid account association
+
plan/capabilities do not include managed runtime
→ ManagedRuntimeAccessDecision = DISABLED
→ OperationalMode = NONE
→ ordinary Windows desktop on SplitOS baseline
```

### 3.2 PRO online

```text
valid account association
+
server-authoritative PRO capability evidence
→ ManagedRuntimeAccessDecision = ENABLED
→ managed runtime may enter WORK xor GAME
```

### 3.3 PRO offline

A bounded cryptographically verifiable offline entitlement assertion MAY authorize the capabilities explicitly listed in that assertion until its expiration.

A local `cachedPro=true` flag is forbidden.

---

## 4. Native application authentication baseline

SplitOS desktop is a **public native OAuth/OIDC client**.

It MUST NOT embed a reusable client secret.

v1 authentication baseline:

```text
Runtime Host / Manager
→ create auth transaction
→ external system browser
→ SplitOS Authorization Server
→ Authorization Code
→ loopback redirect to local client
→ PKCE S256 verification
→ token exchange
→ account identity established
```

Normative requirements:

1. authorization-code flow only;
2. PKCE `S256` required;
3. implicit flow forbidden;
4. external system browser required for interactive login;
5. exact redirect matching except ephemeral loopback port;
6. state value required;
7. OIDC nonce required when ID token is issued;
8. no password collection inside Manager/Game Launcher;
9. authorization code is one-time and short-lived;
10. auth transaction must be correlated to the Windows user/session that initiated it.

Detailed flow is defined in `Native Authentication Flow.md`.

---

## 5. Account association state

Per Windows user, Runtime Host maintains semantic association state:

```text
UNASSOCIATED
AUTHENTICATING
ASSOCIATED
REAUTH_REQUIRED
SIGNING_OUT
```

This state is independent from entitlement tier.

### UNASSOCIATED

No active SplitOS Account association is established for this Windows user.

Windows remains usable. Manager SHOULD expose Sign in / Create account.

### AUTHENTICATING

One interactive auth transaction is active for this Windows user/session.

A second request SHOULD activate/reuse the existing transaction rather than create competing browser transactions.

### ASSOCIATED

A stable `accountId` has been established and local protected session material may exist.

### REAUTH_REQUIRED

The account reference may still be known locally, but reusable authentication evidence cannot be trusted/used.

### SIGNING_OUT

New premium operations MUST be blocked while local credentials and association state are being removed.

---

## 6. First-run behavior

Canonical first-run sequence:

```text
Windows OOBE
→ Windows user created
→ first interactive sign-in
→ Runtime Host starts
→ SplitOS first-run required
→ Manager opens account flow
→ authenticate/create account
→ association persisted
→ entitlement resolved
→ FREE or PRO path
```

### 6.1 Backend unavailable on first run

If backend/auth cannot be reached:

```text
first-run remains incomplete
→ account association not fabricated
→ managed runtime remains disabled
→ Windows desktop remains usable
```

Manager SHOULD offer retry and clearly distinguish product-account setup failure from Windows failure.

---

## 7. Token model

### 7.1 Access token

v1 baseline:

```text
short-lived bearer access token
nominal lifetime: 15 minutes
memory-only in Runtime Host where practical
```

An access token MUST NOT be persisted merely for convenience.

### 7.2 Refresh token

If issued, refresh token:

- is confidential reusable credential material;
- MUST be bound to the public native client/account authorization;
- MUST use refresh-token rotation;
- MUST be protected at rest using user-scoped Windows protection;
- MUST never be logged;
- MUST never be exposed to Manager/Game Launcher;
- MUST be removed/revoked on explicit SplitOS sign-out.

v1 server policy baseline:

```text
inactivity lifetime: 30 days
absolute lifetime: 90 days
```

The server MAY shorten these windows for security/account policy.

### 7.3 Rotation/replay

Every successful refresh MUST issue a new refresh token and invalidate the previous token.

Detected reuse of an invalidated refresh token SHOULD revoke the token family / force reauthentication for that native session.

### 7.4 ID token

If OIDC ID token is used:

- it is authentication evidence, not an entitlement document;
- `sub`/stable account subject identifies the authenticated account;
- email/name claims are display metadata only;
- nonce/audience/issuer/expiration MUST be validated;
- ID token MUST NOT grant PRO by itself.

---

## 8. Local secret protection

Reusable credentials MUST NOT be stored in normal SQLite plaintext fields.

v1 Windows-native baseline:

```text
CryptProtectData / CryptUnprotectData
→ user-scoped DPAPI
```

Protected credential material belongs to the current Windows user and current machine.

Recommended physical location:

```text
%LocalAppData%\SplitOS\Secrets\account.v1.dat
```

File ACL MUST be restricted to the current Windows user and required system/runtime identities.

The DPAPI blob MAY contain:

```text
formatVersion
accountId
refreshToken
refreshTokenFamilyId/reference
lastTrustedServerUtc
offlineEntitlementAssertion
```

but MUST NOT contain Windows passwords or a desktop client secret.

Detailed rules are in `Token and Secret Storage.md`.

---

## 9. Entitlement model

Entitlement is server canonical.

Recommended response model:

```text
entitlementVersion
plan: FREE | PRO
status
validFrom
validUntil
capabilities[]
offlineEligible
```

`plan` is a presentation/product tier. Runtime authorization SHOULD primarily use explicit capability IDs so future tiers do not require rewriting every consumer.

Initial capability family MAY include:

```text
runtime.managed_modes
game.launcher
game.profiles
game.optimization
game.shared_apps
runtime.advanced_device_orchestration
updates.premium_channel
support.premium
```

Exact commercial packaging remains product-owned.

### 9.1 Capability rule

```text
plan == PRO
```

alone MUST NOT be interpreted as authorization if the needed capability is not present/valid.

### 9.2 Entitlement refresh

Runtime Host MUST refresh entitlement:

- after successful login;
- after token refresh when useful;
- when Manager reports checkout completion/return;
- on runtime startup when online;
- before entering premium behavior if current evidence is stale/insufficient;
- periodically while online using a non-aggressive policy.

---

## 10. Offline entitlement baseline

Offline premium access uses `OfflineEntitlementAssertion v1`.

v1 serialization baseline:

```text
JWS Compact Serialization
+
UTF-8 JSON claims
```

Exact signing algorithm, key hierarchy, rotation and revocation are owned by `SPEC-12`.

Required signed claims:

```text
ver
iss
sub                    // accountId
jti
installationId
associationId
entitlementVersion
plan
capabilities[]
iat
nbf
exp
```

### 10.1 Maximum offline validity

Server MUST set:

```text
exp <= min(iat + 7 days, authoritative entitlement validUntil)
```

A cancelled subscription may therefore remain offline-usable only through already-paid/authorized time and never beyond assertion expiry.

### 10.2 Local binding

The assertion MUST be checked against:

```text
current accountId
current SplitOS installationId
current local associationId
signature/key trust
nbf/exp
expected issuer
known/revoked key policy
```

The assertion SHOULD additionally be kept inside the user-scoped DPAPI blob to make simple cross-machine/user copying ineffective within the v1 threat model.

### 10.3 Clock rollback

Runtime Host maintains protected `lastTrustedServerUtc` evidence.

If local UTC moves materially behind trusted time:

```text
currentUtc < lastTrustedServerUtc - 5 minutes
```

or falls outside assertion validity with the same skew tolerance:

```text
CLOCK_ROLLBACK_SUSPECTED
→ offline PRO not trusted
→ online validation required
```

This is abuse resistance within the v1 ordinary-user-process threat model; an unrestricted hostile local Administrator remains outside the v1 guarantee.

---

## 11. Subscription / checkout behavior

Manager may initiate subscription upgrade through SplitOS Backend.

Canonical chain:

```text
Manager
→ Runtime Host
→ create backend checkout session
→ open hosted checkout in browser
→ Payment Provider
→ backend validates payment evidence
→ backend updates Entitlement
→ Runtime Host refreshes Entitlement
→ ManagedRuntimeAccessDecision recomputed
```

A browser URI, checkout return page, local callback or UI message MUST NOT directly grant PRO.

The desktop may treat such a callback only as:

```text
REFRESH_ENTITLEMENT_NOW
```

---

## 12. Sign-out behavior

Explicit SplitOS sign-out MUST:

1. stop creation of new premium operations;
2. request safe managed-runtime convergence where needed;
3. revoke current refresh/session token server-side when online, best effort;
4. securely remove local DPAPI credential blob;
5. clear active Windows-user account association;
6. invalidate offline entitlement evidence;
7. recompute runtime access to base/disabled state;
8. preserve ordinary Windows usability.

User-created Game Profiles/preferences SHOULD NOT be destructively deleted by sign-out unless the user explicitly chooses `Remove local SplitOS data`.

Detailed mode-state convergence belongs to `SPEC-05`.

---

## 13. Failure behavior

| Failure | Required outcome |
|---|---|
| browser auth cancelled | remain UNASSOCIATED/previous account state; no fake failure of Windows |
| state/nonce mismatch | reject auth result, destroy transaction |
| PKCE exchange fails | no account association established |
| backend unavailable | use valid bounded offline evidence if available; otherwise premium disabled |
| access token expired | refresh or reauth; never infer PRO from stale access token |
| refresh token revoked/reused | REAUTH_REQUIRED |
| DPAPI decrypt fails | destroy unusable local credential reference and require reauth |
| entitlement FREE | managed runtime disabled, base Windows usable |
| offline assertion expired | premium disabled until online proof obtained |
| assertion signature/claims invalid | reject assertion; audit trust failure |
| local clock rollback suspected | require online validation |
| payment callback received but backend entitlement unchanged | remain current entitlement; keep refresh/retry UI |

---

## 14. Privacy/data minimization

Desktop SHOULD persist only data required for local product behavior.

MUST NOT log:

```text
access token
refresh token
authorization code
PKCE verifier
full offline assertion unless explicitly redacted/safe
payment card data
password
```

Account API responses SHOULD expose the minimum user profile data required by Manager.

Payment card data remains payment-provider territory.

---

## 15. Acceptance criteria

SPEC-04 conforms only if all are demonstrable:

1. Windows login works independently of SplitOS backend availability;
2. native login uses external browser + authorization code + PKCE S256;
3. desktop ships no reusable client secret;
4. account identity uses stable server account ID, not email;
5. one Windows user has at most one active SplitOS account association;
6. access token is short-lived and not treated as entitlement persistence;
7. refresh token rotation/replay handling exists;
8. reusable token storage is user-scoped DPAPI-protected;
9. FREE cannot accidentally enable managed runtime;
10. PRO requires server proof or valid bounded offline assertion;
11. offline assertion is signature/context/time validated;
12. clock rollback causes online revalidation rather than extended offline access;
13. checkout/browser callback never directly grants PRO;
14. explicit sign-out removes credentials/offline proof and safely disables premium behavior;
15. auth/entitlement failure never prevents base Windows desktop usage.

---

## 16. Explicit OPEN carried forward

- concrete backend implementation/provider technology;
- exact authorization server deployment topology;
- payment provider choice;
- exact commercial device-count/seat policy;
- exact JWS signature algorithm/key IDs/rotation/revocation (`SPEC-12`);
- whether later versions add TPM/CNG device proof beyond DPAPI + installation/association binding;
- exact runtime transition on entitlement loss while actively in GAME (`SPEC-05`);
- remote account/profile synchronization beyond account identity (`future/product decision`).

---

## 17. Engineering evidence

- OAuth 2.0 for Native Apps (RFC 8252): https://www.rfc-editor.org/rfc/rfc8252
- PKCE (RFC 7636): https://www.rfc-editor.org/rfc/rfc7636
- OAuth 2.0 Security Best Current Practice (RFC 9700): https://www.rfc-editor.org/rfc/rfc9700
- OpenID Connect Core 1.0: https://openid.net/specs/openid-connect-core-1_0.html
- DPAPI `CryptProtectData`: https://learn.microsoft.com/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata
- DPAPI `CryptUnprotectData`: https://learn.microsoft.com/windows/win32/api/dpapi/nf-dpapi-cryptunprotectdata
