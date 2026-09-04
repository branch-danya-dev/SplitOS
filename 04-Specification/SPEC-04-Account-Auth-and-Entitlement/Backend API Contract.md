# SPEC-04 — Backend API Contract

## 1. Purpose

This document defines the minimum semantic HTTPS contract between SplitOS Runtime Host/Manager and SplitOS Account Backend.

It intentionally defines resource semantics, authorization and error behavior without selecting a backend programming language/framework.

---

## 2. Transport

All non-loopback backend communication MUST use HTTPS.

Runtime Host is the desktop API client for authenticated product APIs.

Manager SHOULD request backend actions through Runtime Host rather than owning tokens itself.

Common request metadata:

```text
Authorization: Bearer <access-token>
X-SplitOS-Client-Version: <release>
X-SplitOS-Installation-Id: <installationId>
X-Correlation-Id: <correlationId>
```

Sensitive endpoints MUST NOT accept credentials/tokens in URL query parameters.

---

## 3. Authorization server metadata

Backend/auth deployment SHOULD expose standard OIDC/OAuth metadata, for example:

```text
/.well-known/openid-configuration
```

Desktop obtains/validates configured:

```text
issuer
authorization_endpoint
token_endpoint
jwks_uri
revocation_endpoint if supported
```

Production issuer/endpoints MUST be release/configuration-owned trusted values, not arbitrary user URLs.

---

## 4. Native OAuth client registration

v1 logical client:

```text
client_id = splitos-windows-native-v1
client_type = public
client_secret = none
redirect = http://127.0.0.1:<dynamic>/oauth/callback
PKCE = required S256
```

Actual client ID string is deployment configuration; semantics above are normative.

---

## 5. Account endpoint

### `GET /v1/account`

Purpose: obtain canonical account profile/status for current authenticated subject.

Representative response:

```json
{
  "accountId": "acc_...",
  "status": "ACTIVE",
  "displayName": "Daniel",
  "email": "user@example.com",
  "emailVerified": true,
  "createdUtc": "..."
}
```

Rules:

- `accountId` is canonical identity;
- email/displayName are mutable display metadata;
- desktop MUST NOT infer entitlement from account profile fields.

Possible semantic errors:

```text
AUTH_REQUIRED
ACCOUNT_DISABLED
ACCOUNT_NOT_FOUND
```

---

## 6. Current entitlement endpoint

### `GET /v1/entitlements/current`

Representative response:

```json
{
  "accountId": "acc_...",
  "entitlementVersion": 42,
  "plan": "PRO",
  "status": "ACTIVE",
  "validFrom": "...",
  "validUntil": "...",
  "capabilities": [
    "runtime.managed_modes",
    "game.launcher",
    "game.profiles"
  ],
  "offlineEligible": true,
  "serverUtc": "..."
}
```

`entitlementVersion` MUST monotonically identify newer entitlement state for the same account sufficiently to supersede local stale evidence.

Backend SHOULD include trusted server time in response/body or another authenticated application-level mechanism used by Runtime Host clock evidence.

---

## 7. Offline assertion endpoint

### `POST /v1/entitlements/offline-assertion`

Authorization: current authenticated account required.

Request:

```json
{
  "installationId": "...",
  "associationId": "...",
  "requestedCapabilities": ["runtime.managed_modes"]
}
```

Response:

```json
{
  "assertion": "<JWS compact>",
  "issuedUtc": "...",
  "expiresUtc": "...",
  "entitlementVersion": 42,
  "serverUtc": "..."
}
```

Backend MUST:

- verify requested installation/account association policy;
- issue only currently authorized capabilities;
- cap expiry according to SPEC-04 offline policy;
- never accept client-provided expiry;
- sign with an active trusted entitlement signing key.

Possible errors:

```text
OFFLINE_NOT_ELIGIBLE
INSTALLATION_LIMIT_REACHED
ENTITLEMENT_NOT_ACTIVE
CAPABILITY_NOT_ALLOWED
AUTH_REQUIRED
```

---

## 8. Checkout session endpoint

### `POST /v1/subscription/checkout-sessions`

Purpose: create hosted upgrade/management flow.

Request:

```json
{
  "targetOfferId": "pro-default",
  "returnContext": "manager-upgrade",
  "idempotencyKey": "..."
}
```

Response:

```json
{
  "checkoutSessionId": "chk_...",
  "checkoutUrl": "https://...",
  "expiresUtc": "..."
}
```

Rules:

- checkout URL MUST be backend/provider controlled;
- Manager opens URL in system browser;
- endpoint MUST be idempotent for repeated same idempotency key;
- card/payment credentials never traverse Runtime Host.

---

## 9. Checkout status endpoint

### `GET /v1/subscription/checkout-sessions/{checkoutSessionId}`

Representative response:

```json
{
  "checkoutSessionId": "chk_...",
  "status": "PENDING | COMPLETED | CANCELLED | EXPIRED",
  "entitlementVersion": 43,
  "serverUtc": "..."
}
```

`COMPLETED` means backend has correlated/validated provider result; Runtime Host still fetches `/v1/entitlements/current` before changing runtime access.

This endpoint prevents any browser return callback from becoming entitlement authority.

---

## 10. Session/token revocation

Backend/auth server SHOULD expose a standard revocation or authenticated session-end mechanism.

Explicit desktop sign-out uses:

```text
best-effort refresh/session revoke
+
mandatory local credential deletion
```

If backend cannot be reached during sign-out, local credentials are still removed immediately; server token expires/revokes according to server policy later.

---

## 11. Installation association policy endpoint — optional v1

If backend requires explicit device/installation registration, semantic contract MAY be:

### `POST /v1/installations/associate`

```json
{
  "installationId": "...",
  "associationId": "...",
  "clientVersion": "..."
}
```

Response MAY include:

```text
association status
device-limit status
friendly installation label
lastSeenUtc
```

Exact device management UI/API may be deferred, but offline assertion endpoint must have sufficient server-side policy context.

---

## 12. Error envelope

Non-OAuth product API errors SHOULD use a stable envelope:

```json
{
  "error": {
    "code": "ENTITLEMENT_NOT_ACTIVE",
    "message": "Human-safe localized/display message or key",
    "retryable": false,
    "correlationId": "...",
    "details": {}
  }
}
```

Desktop behavior MUST use `code`, not parse human message text.

### Common codes

```text
AUTH_REQUIRED
TOKEN_REVOKED
ACCOUNT_DISABLED
ENTITLEMENT_NOT_ACTIVE
OFFLINE_NOT_ELIGIBLE
INSTALLATION_LIMIT_REACHED
CHECKOUT_EXPIRED
RATE_LIMITED
TEMPORARILY_UNAVAILABLE
INVALID_REQUEST
```

---

## 13. HTTP semantics

Recommended mapping:

```text
200/201 → successful resource/result
400     → invalid semantic request
401     → missing/invalid authentication
403     → authenticated but capability/policy denied
404     → addressed resource absent/not visible
409     → state/idempotency/version conflict
429     → rate limited
5xx     → backend/provider/internal temporary failure
```

Desktop MUST NOT map all `403` to FREE or all `500` to account invalidation.

---

## 14. Retry policy

Safe GET requests MAY use bounded exponential backoff with jitter.

Mutating POST operations MUST be idempotent where retries are possible.

Checkout-session create uses explicit idempotency identity.

Auth code exchange MUST NOT be blindly retried after ambiguous response because authorization code is one-time; the auth module decides whether a new interactive flow is required.

---

## 15. Caching

Account display metadata MAY be cached locally.

Entitlement response MAY be cached as evidence but MUST retain:

```text
entitlementVersion
observedUtc
serverUtc
validUntil
source = ONLINE_BACKEND
```

Cache does not become server authority.

---

## 16. Versioning

Product APIs use URI major version:

```text
/v1/...
```

Breaking semantic changes require a new major API or explicitly negotiated contract.

Additive JSON response fields MAY be ignored unless declared critical.

Client MUST reject entitlement/offline assertion versions it cannot safely interpret.

---

## 17. Acceptance criteria

1. all authenticated product API calls are HTTPS Bearer-token calls from Runtime Host;
2. account and entitlement are separate resources;
3. canonical account key is accountId;
4. checkout endpoint never receives card data;
5. checkout completion still requires entitlement refresh;
6. offline assertion expiry/capabilities are server-controlled;
7. stable error codes exist independently of display strings;
8. retries do not create duplicate checkout/payment operations;
9. entitlement cache includes provenance/version/freshness;
10. arbitrary user-provided issuer/backend URLs cannot redefine production trust.
