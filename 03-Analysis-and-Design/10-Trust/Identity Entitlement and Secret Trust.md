# SplitOS — Identity, Entitlement and Secret Trust

## 1. Purpose

Документ определяет trust rules для SplitOS Account, authentication, entitlement, offline capability access, локального хранения секретов и payment evidence.

Каноническое разделение:

```text
Windows identity
!= SplitOS Account identity
!= SplitOS entitlement
!= payment transaction
```

Каждый слой имеет своего authority owner.

---

## 2. Identity authority

### Windows identity

Windows владеет:

- Windows user/session identity;
- SID/logon session;
- OS authentication.

SplitOS использует это как локальный user-context evidence.

### SplitOS identity

SplitOS Backend / Product Identity & Entitlement владеет:

- SplitOS Account identity;
- account session/token issuance;
- account status;
- server-side entitlement.

### Association

SplitOS локально хранит связь:

```text
WindowsUserContext
↔ SplitOSAccount
```

но эта association не превращает Windows Account в SplitOS Account и наоборот.

---

## 3. Authentication design principle

Для native desktop application предпочтительная baseline-модель:

```text
SplitOS Manager / Runtime
→ system browser / external user-agent
→ SplitOS authorization endpoint
→ authorization code
→ native app
→ token endpoint
```

с PKCE для public native client.

Почему:

- desktop app не должен собирать пароль SplitOS внутри собственного embedded webview;
- browser сохраняет отдельный authentication security domain;
- intercepted authorization code без PKCE verifier не должен быть достаточен.

Exact OAuth/OIDC provider/schema остаётся implementation decision, но embedded credential capture не является preferred design.

---

## 4. Authentication flow trust

Conceptual flow:

```text
1. Runtime creates auth transaction
2. generate state + PKCE verifier/challenge
3. open external browser
4. user authenticates at SplitOS backend
5. callback arrives
6. validate callback belongs to outstanding transaction
7. redeem code using verifier
8. backend issues tokens
9. account context established
```

Callback alone is never proof of authentication success.

---

## 5. Callback trust

Custom URI / loopback / app-link callback is transport to resume the flow, not authority.

Required checks include conceptually:

- outstanding auth transaction exists;
- state/correlation matches;
- redirect target matches expected flow;
- authorization code is redeemed server-side/token endpoint;
- PKCE verifier matches;
- transaction is not expired/already consumed.

Forbidden:

```text
splitos://login-success?account=123&pro=true
→ trust query string
```

---

## 6. Token classes

Conceptually distinguish:

### Access token

Shorter-lived token used to call SplitOS backend.

### Refresh credential/token

Longer-lived secret enabling token renewal.

### Offline entitlement assertion

Optional server-issued locally verifiable evidence allowing bounded premium continuation without backend availability.

These must not be collapsed into one permanent `licenseKey` string stored in plaintext.

---

## 7. Secret storage

Plaintext storage prohibited for reusable credentials.

Bad:

```text
%APPDATA%/splitos/config.json
{
  "refreshToken": "..."
}
```

Preferred Windows-native protection candidate:

```text
DPAPI user-bound protection
```

for credentials tied to a specific Windows user context.

DPAPI normally binds decryptability to the same Windows logon credentials/machine context and provides integrity checking on unprotect.

Alternative/adjacent Windows credential storage may be evaluated during implementation.

---

## 8. User vs machine scope

Because SplitOS Account association is Windows-user-specific, user-bound secret protection is preferred for account credentials.

Machine-wide secrets, if ever required for service/update trust, must not be reused as user account secrets.

```text
User account token
→ user scope

Broker/release verification public keys
→ machine product configuration
```

Private release-signing keys never belong on client machine.

---

## 9. Token exposure rules

Tokens must not be written to:

- diagnostics;
- crash logs;
- command line arguments;
- URI query logs;
- Game Client metadata;
- plaintext registry/settings;
- IPC payloads unless strictly required and protected by design.

UI should not display reusable credentials.

---

## 10. Entitlement authority

Canonical entitlement is issued by SplitOS Product Identity & Entitlement server-side domain.

Client receives evidence/projection.

```text
Backend Entitlement
→ signed/authenticated response/assertion
→ local ManagedRuntimeAccessDecision
```

Local UI/configuration cannot create entitlement.

Forbidden:

```text
settings.json:
  pro=true
```

as authority.

---

## 11. Entitlement semantics

Entitlement should describe capabilities, not only marketing plan name.

Conceptually:

```text
Account entitlement
├── managed_runtime
├── game_launcher
├── game_profiles
├── optimization
├── update_channel
└── future capabilities
```

A plan label such as `PRO` may map to capabilities but authorization should ultimately consume explicit effective capabilities/versioned product policy.

---

## 12. Runtime access decision

The existing domain remains:

```text
Entitlement evidence
+
local validity/offline policy
+
runtime compatibility
→ ManagedRuntimeAccessDecision
```

Possible result:

```text
ENABLED
DISABLED
DEGRADED
REAUTH_REQUIRED
```

Trust layer adds:

- evidence issuer validated;
- evidence integrity validated;
- validity/freshness validated;
- account/user/installation context binding validated where required.

---

## 13. FREE behavior as security fallback

If premium trust cannot be established safely:

```text
ManagedRuntime = DISABLED/DEGRADED
```

must not imply:

```text
Windows login blocked
```

Canonical fallback remains:

```text
base Windows experience usable
```

This allows fail-closed premium authorization without making the PC hostage to backend availability.

---

## 14. Offline entitlement

Offline premium continuation requires explicit server-issued evidence, not a cached boolean.

Conceptual assertion must include/bind at least candidates:

- issuer;
- subject/account;
- capability set;
- issued-at;
- expiry/valid-until;
- product/schema version;
- installation/device binding if policy requires;
- unique assertion identifier/key id;
- cryptographic signature/authenticity proof.

Exact format is OPEN.

Could be a signed structured assertion or another cryptographically verifiable format.

---

## 15. Offline entitlement validation

Conceptual validation:

```text
load assertion
→ validate schema/version
→ validate issuer/key
→ validate signature/integrity
→ validate subject/account association
→ validate validity window
→ validate device/installation binding if present
→ derive allowed capabilities
```

Any failure:

```text
premium capability not proven
→ do not grant premium capability
```

while base Windows remains available.

---

## 16. Clock trust

Offline expiration introduces clock manipulation risk.

Simple rule:

```text
current local wall clock
```

may be insufficient against deliberate rollback.

Potential signals/candidates:

- server-observed last-good time;
- monotonic/boot-relative evidence where useful;
- Windows secure/time services;
- bounded grace windows;
- online refresh after suspicious rollback.

Exact anti-clock-rollback strategy remains OPEN and requires Specification/testing.

Trust model must not claim offline expiry is tamper-proof before this is solved.

---

## 17. Sign-out

SplitOS sign-out should:

- invalidate/remove local account association as intended;
- remove local reusable account credentials;
- clear/retire offline entitlement evidence as policy requires;
- not delete Windows user data/game profiles unless user explicitly chooses such action;
- converge premium runtime safely to base experience if necessary.

Server token revocation behavior remains backend specification.

---

## 18. Multi-user isolation

On same PC:

```text
Windows User A
↔ SplitOS Account A

Windows User B
↔ SplitOS Account B
```

User A must not automatically gain access to User B credentials/entitlement cache/profile secrets.

Local storage ACL + user-bound secret protection should reflect Windows user isolation.

Machine-wide release/update metadata may be shared, but account secrets must not.

---

## 19. Payment trust

Payment Provider owns payment transaction evidence.

SplitOS Backend owns product entitlement decision.

Canonical chain:

```text
Payment Provider
→ authenticated provider-to-backend evidence/webhook/API result
→ backend validates transaction
→ backend updates SplitOS entitlement
→ client refreshes entitlement
```

Desktop callback/browser return is only UX continuation signal.

---

## 20. Payment replay/forgery

Backend payment processing must conceptually account for:

- provider authenticity;
- event/transaction identifier;
- replay/idempotency;
- amount/product/currency mapping where relevant;
- completed vs pending/refunded/cancelled state;
- account/order binding.

Client must never parse a browser URL and grant paid capability directly.

---

## 21. Subscription expiry/downgrade

When entitlement changes:

```text
backend canonical entitlement
→ client refresh/offline expiry
→ ManagedRuntimeAccessDecision
→ safe convergence
```

Local premium state/data may remain stored, but capability execution is gated.

User Game Profiles need not be deleted when subscription expires.

This allows later reactivation without data loss.

---

## 22. Account/backend unavailable

Failure modes:

### No network but valid offline evidence

Permit only capabilities allowed by offline policy.

### No network and no valid offline evidence

Premium access not proven → base experience.

### Backend response cannot be authenticated/validated

Treat as trust failure, not as `FREE` authoritative server response.

### Local token cannot be decrypted

Require reauthentication; do not attempt plaintext fallback.

---

## 23. Secret lifecycle

Reusable secret lifecycle:

```text
issue
→ protected storage
→ load only when needed
→ refresh/rotate
→ revoke/expire
→ secure deletion best effort
```

Token rotation should allow old token retirement without reinstall.

Exact refresh token rotation policy is backend specification.

---

## 24. Backend transport

All account/token/entitlement network communication requires authenticated encrypted transport (HTTPS/TLS).

Transport security is necessary but not sufficient for offline package/artifact authenticity.

For normal API responses, TLS server authentication plus application authorization is current baseline.

Certificate pinning is not assumed by default; if considered later, rotation/recovery implications must be designed explicitly.

---

## 25. Client authenticity to backend

Installed desktop client is a public/native client and cannot safely keep a universal application secret hidden from a determined local user/admin.

Therefore:

```text
embedded static client_secret
```

must not be treated as proof that request came from an uncompromised SplitOS install.

Backend trust should rely on user authentication, issued tokens, server-side policy and optional device/install attestation only if a real supported mechanism is designed.

---

## 26. Device registration

Potential future object:

```text
DeviceRegistration / InstallationRegistration
```

may support:

- account device list;
- offline entitlement binding;
- subscription device limits;
- security revocation.

But exact stable hardware identity mechanism is OPEN.

Do not bind product identity to fragile single identifiers such as raw MAC address alone.

---

## 27. Security invariants

### ID-INV-001

Windows password and SplitOS Account password are not intercepted/reused by SplitOS runtime.

### ID-INV-002

Native login callback cannot establish account session without server token exchange/validation.

### ID-INV-003

Reusable account tokens are not stored plaintext.

### ID-INV-004

Local editable configuration cannot grant premium entitlement.

### ID-INV-005

Offline premium use requires cryptographically/authentically verifiable bounded evidence.

### ID-INV-006

Payment client callback cannot issue entitlement.

### ID-INV-007

Entitlement trust failure denies premium capability but preserves base Windows usability.

### ID-INV-008

User A account secrets are not automatically accessible to User B.

### ID-INV-009

Release-signing private keys never ship in client runtime.

### ID-INV-010

A native app static client secret is not treated as a meaningful security secret.

---

## 28. Research basis

Native-app OAuth best practice requires an external user-agent and PKCE for public native clients.

Windows DPAPI provides user/machine-context data protection suitable as a candidate for local reusable token protection.

These mechanisms define current candidates, not final backend product choice.

---

## 29. Open questions

- SplitOS Account provider / OAuth-OIDC implementation;
- redirect URI strategy on Windows;
- access/refresh token lifetimes;
- refresh token rotation/revocation;
- exact DPAPI/Credential Manager abstraction;
- offline entitlement format;
- offline TTL/grace period;
- installation/device binding;
- secure clock rollback policy;
- multi-device subscription policy;
- sign-out data retention policy;
- account recovery/MFA policy;
- device revocation behavior;
- entitlement capability schema/versioning.

---

## 30. Result

Canonical identity trust chain:

```text
Windows user context
→ external-browser SplitOS authentication
→ server-issued account tokens
→ protected local storage
→ server/offline entitlement evidence
→ validated ManagedRuntimeAccessDecision
→ FREE or allowed PRO capabilities
```

Identity, payment and entitlement remain separated throughout the chain.