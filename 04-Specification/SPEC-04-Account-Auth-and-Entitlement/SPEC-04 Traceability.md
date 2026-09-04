# SPEC-04 — Traceability and Verification Backlog

## 1. Purpose

This document records Specification-level decisions introduced by SPEC-04 and maps them back to the established A&D baseline.

---

## 2. Primary A&D sources

```text
02-Requirements/
03-Analysis-and-Design/02-Ownership/
03-Analysis-and-Design/03-States/Runtime Access State Model
03-Analysis-and-Design/04-Behavior/First Run and Runtime Access Behavior
03-Analysis-and-Design/05-Data/Identity and Runtime Access Data Model
03-Analysis-and-Design/06-Interfaces/
03-Analysis-and-Design/08-Flows/First Run and Subscription Flow
03-Analysis-and-Design/09-Failures/
03-Analysis-and-Design/10-Trust/Identity Entitlement and Secret Trust.md
03-Analysis-and-Design/11-Synthesis/
```

SPEC-04 preserves:

```text
Windows Account != SplitOS Account
SplitOS Account != Entitlement
FREE != PRO
base Windows usability > premium capability availability
```

---

## 3. Specification decisions

### SPEC-DEC-015

v1 desktop authentication uses OAuth/OIDC Authorization Code through the external system browser.

### SPEC-DEC-016

Public native client uses mandatory PKCE `S256`; desktop contains no reusable client secret.

### SPEC-DEC-017

v1 Windows redirect uses loopback IP literal:

```text
http://127.0.0.1:<ephemeral>/oauth/callback
```

with one listener per auth transaction.

### SPEC-DEC-018

Access-token nominal lifetime is 15 minutes; access token is memory-only where practical.

### SPEC-DEC-019

Refresh tokens rotate on every successful refresh; v1 server policy upper bounds are 30-day inactivity and 90-day absolute lifetime.

### SPEC-DEC-020

Reusable desktop auth material uses user-scoped DPAPI; Manager/Game Launcher never receive refresh token.

### SPEC-DEC-021

Offline premium access uses signed `OfflineEntitlementAssertion v1`, JWS Compact JSON claims, bound to account + installation + association, with maximum 7-day validity and 5-minute clock skew.

### SPEC-DEC-022

Runtime authorizes premium behavior using explicit entitlement capabilities; tier string alone is insufficient.

### SPEC-DEC-023

One Windows user profile has at most one active SplitOS Account association; account switch creates a new `associationId`.

### SPEC-DEC-024

Hosted checkout/browser completion never grants PRO directly; desktop must refresh backend entitlement and use the newer server entitlement version.

### SPEC-DEC-025

When offline premium authorization cannot be proven, managed runtime fails closed while ordinary Windows remains usable.

---

## 4. A&D mapping

| SPEC-04 decision | Canonical source |
|---|---|
| Windows identity separate from SplitOS identity | Ownership + Concept + Runtime Access |
| SplitOS Account after Windows first sign-in | First Run Behavior / FL-01 |
| external browser authentication | Trust / Identity Entitlement and Secret Trust |
| PKCE native-client flow | Trust candidate closed in SPEC-04 |
| server-owned entitlement | Ownership / Data / Trust |
| FREE baseline Windows experience | Runtime Access State + Requirements |
| PRO enables managed runtime | Runtime Access State + Requirements |
| offline assertion bounded/fail-closed | Trust + Failures |
| payment provider not entitlement owner | Ownership + Interfaces + Trust |
| callback does not grant PRO | External Evidence Trust / FL-01 |
| DPAPI user secret protection | Trust candidate closed in SPEC-04 |
| account association per Windows user | Data Model + Synthesis placement |

---

## 5. Verification backlog for SPEC-14

### Authentication

```text
V-AUTH-001 external browser is used; embedded password UI absent
V-AUTH-002 PKCE S256 required; plain rejected
V-AUTH-003 loopback listener binds only 127.0.0.1
V-AUTH-004 auth state mismatch rejected
V-AUTH-005 OIDC nonce mismatch rejected
V-AUTH-006 authorization code replay rejected
V-AUTH-007 auth transaction timeout destroys local verifier/state
V-AUTH-008 no desktop client secret exists in install/config
```

### Token / secret handling

```text
V-AUTH-009 refresh token absent from SQLite/logs/UI IPC
V-AUTH-010 DPAPI blob cannot be decrypted by another normal Windows user
V-AUTH-011 refresh-token rotation stores new token before continued use
V-AUTH-012 refresh-token reuse signal forces REAUTH_REQUIRED
V-AUTH-013 DPAPI decrypt failure requires reauthentication
V-AUTH-014 sign-out removes local protected session material
```

### Entitlement

```text
V-ENT-001 FREE entitlement cannot enable runtime.managed_modes
V-ENT-002 PRO requires explicit capability
V-ENT-003 backend entitlement version supersedes local stale evidence
V-ENT-004 offline assertion bad signature rejected
V-ENT-005 assertion wrong account rejected
V-ENT-006 assertion wrong installation rejected
V-ENT-007 assertion wrong association rejected
V-ENT-008 assertion >7-day requested/forged expiry rejected
V-ENT-009 expired assertion disables premium when backend unavailable
V-ENT-010 clock rollback suspicion blocks offline premium
V-ENT-011 ordinary Windows remains usable when entitlement cannot be proven
```

### Account / checkout

```text
V-ACC-001 one active association per Windows user
V-ACC-002 account switch creates new associationId and invalidates old proof
V-ACC-003 email change does not create new canonical identity
V-ACC-004 sign-out preserves Game Profiles by default
V-PAY-001 browser checkout return alone does not enable PRO
V-PAY-002 backend COMPLETED checkout still requires entitlement refresh
V-PAY-003 duplicate checkout create idempotency does not create duplicate purchase session
```

---

## 6. Dependencies on adjacent specs

### SPEC-03

When merged, physical storage must provide:

```text
user association metadata
non-secret entitlement evidence metadata
protected secret reference/adjacent DPAPI path
```

SPEC-04 does not require refresh token plaintext in SQLite.

### SPEC-05

Defines safe state convergence when:

```text
PRO → FREE
PRO entitlement expires
user signs out while GAME/WORK managed runtime active
```

SPEC-04 only decides authorization result.

### SPEC-12

Defines:

```text
JWS signing algorithm
entitlement signing key hierarchy
rotation/revocation
key compromise response
```

SPEC-04 defines signed claims and validation semantics.

### SPEC-14

Turns verification backlog above into executable acceptance/security tests.

---

## 7. Current OPEN

```text
commercial active-device numeric limit
backend implementation framework/provider
payment provider
TPM/CNG device proof beyond v1 DPAPI/context binding
exact release key algorithm/hierarchy
profile cloud sync policy
```

These OPEN items do not permit implementation to weaken the account/entitlement ownership rules defined here.
