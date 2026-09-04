# SplitOS — Account, Payment, Builder and Update Integration

## 1. Purpose

Документ связывает SplitOS runtime с четырьмя внешними integration domains:

```text
SplitOS Account Backend
Payment Provider
SplitOS Media Builder / Windows servicing
SplitOS Update lifecycle
```

Главная цель — не смешать:

```text
identity
payment evidence
entitlement
Windows source
compatibility
update execution
```

---

# 2. Account backend integration

## 2.1 Runtime direction

```text
SplitOS Manager / Runtime Host
        ↓ HTTPS
SplitOS Account Backend
```

### Status

`CANDIDATE / baseline`.

### Required semantic operations

```text
Authenticate / establish session
Read account profile
Read entitlement evidence
Refresh entitlement
Sign out / revoke local session
Read subscription-management link/state
```

Exact endpoint names are not canonical yet.

---

## 2.2 Authentication flow

Exact identity protocol remains intentionally OPEN until Trust analysis.

Preferred properties:

```text
no raw long-lived password storage in desktop runtime
browser/system-mediated authentication where appropriate
short-lived access credentials
refresh/re-auth semantics
revocation support
secure local credential storage
```

Potential technologies:

```text
OAuth 2.0 / OpenID Connect style authorization flow
passkey/first-party identity provider
other first-party auth
```

No selection becomes canonical before threat/trust analysis.

---

## 2.3 Local token/session storage

Status: `OPEN`.

Constraints:

- scoped to Windows user context;
- protected from other local users;
- not stored as plaintext configuration;
- revocable;
- expiration/freshness explicit;
- account session must not become Windows authentication authority.

Likely Windows credential-protection mechanisms should be evaluated in Trust layer.

---

# 3. Entitlement synchronization

Canonical sequence:

```text
Backend account/subscription state
→ entitlement evidence
→ local Product Identity & Entitlement
→ ManagedRuntimeAccessDecision
```

### Refresh triggers

```text
first sign-in
SplitOS startup
explicit user refresh
checkout completion
periodic freshness threshold
backend change notification if later supported
before capability requiring fresh entitlement where policy demands
```

### Offline behavior

Local cache may be used according to future offline-entitlement policy.

```text
backend unavailable
!= automatically FREE
!= Windows access denied
```

Exact cache validity remains OPEN.

---

# 4. Payment integration

## 4.1 Preferred architecture

```text
Manager
→ SplitOS Backend: create checkout
→ hosted checkout URL/session
→ system browser / provider page
→ Payment Provider
→ backend-side webhook/callback
→ backend validates payment evidence
→ entitlement updated
→ local runtime refreshes entitlement
```

### Status

`CANDIDATE / preferred`.

---

## 4.2 Why hosted checkout

Keeps desktop runtime outside unnecessary raw card-data processing.

SplitOS desktop should not receive/store:

```text
full card number
CVV
raw payment credentials
```

unless future business/legal requirements explicitly demand a different architecture.

---

## 4.3 Desktop return signal

A browser redirect such as:

```text
splitos://checkout-complete
```

may be useful for UX, but it is not payment authority.

Correct interpretation:

```text
browser callback
→ trigger entitlement refresh
```

not:

```text
browser callback
→ set PRO=true
```

URI input must be treated as untrusted.

---

## 4.4 Payment provider webhook

Backend-side provider notification is the preferred authoritative evidence path.

Pattern:

```text
Provider notification
→ verify authenticity/idempotency
→ update subscription/payment projection
→ derive/update SplitOS entitlement
```

Exact provider and webhook signing scheme are future implementation decisions.

---

# 5. SplitOS custom URI integration

SplitOS may register a local URI scheme for safe navigation/return flows.

Potential uses:

```text
checkout return
account-auth return
open specific Manager page
```

### Status

`CANDIDATE`.

### Security rule

Custom URI activation cannot establish caller identity by itself.

Therefore URI payloads must be validated and must not directly authorize privileged/canonical changes.

---

# 6. Media Builder integration

## 6.1 Core build path

```text
Microsoft-authorized Windows source
        ↓
source validation
        ↓
mount/select image
        ↓
execute SplitOS Build Manifest
        ↓
DISM/offline servicing + package/policy provisioning
        ↓
install SplitOS packages/assets
        ↓
baseline validation
        ↓
prepare installation media/output
```

### Status

`CANDIDATE with VERIFIED Windows servicing mechanisms`.

---

## 6.2 DISM integration

Official Microsoft servicing mechanisms support offline image operations for:

```text
Windows features
Windows packages
provisioned AppX packages
some drivers
unattend offlineServicing
```

### Baseline mechanism

Use DISM or supported DISM APIs/tooling as the primary servicing family where the target component type is supported.

### Important

Not every desired `REMOVE` item maps to the same DISM operation.

Component Matrix must preserve type:

```text
FEATURE
PACKAGE
APPX
DRIVER
SERVICE
TASK
POLICY
OTHER
```

Integration executor maps classification to type-specific action.

---

# 7. Build Manifest executor

The Builder should not contain hard-coded product logic scattered through imperative code.

Preferred pattern:

```text
Versioned Build Manifest
        ↓
Manifest validator
        ↓
Typed action executor
        ↓
Windows servicing mechanism
        ↓
verification evidence
```

Conceptual action examples:

```text
RemoveProvisionedApp
DisableFeature
RemoveFeaturePayload
RemovePackage
ApplyOfflineRegistryPolicy
InstallSplitOSPackage
StageRecoveryAsset
ValidateComponentState
```

Exact schema belongs to Specification later.

---

## 7.1 Idempotency

Where practical, build actions should be idempotent or detect already-achieved state.

Example:

```text
expected REMOVE
actual absent
→ SUCCESS_ALREADY_APPLIED
```

instead of failure.

---

## 7.2 Build failure semantics

Mandatory action failure must not silently produce a baseline labelled supported.

```text
mandatory transformation failed
→ build FAILED / UNSUPPORTED
```

Optional/conditional actions need explicit policy.

---

# 8. Source validation

Before modification, Builder should validate source identity against Compatibility Management knowledge.

Potential evidence:

```text
edition
architecture
Windows build/version
image index/name
source integrity metadata
language where relevant
```

Exact cryptographic/media-source validation policy remains OPEN.

### Rule

Unknown source must not silently become supported because DISM can mount it.

---

# 9. Windows update integration

SplitOS does not redefine Microsoft as source of Windows patches.

Correct chain:

```text
Microsoft publishes patch/update
→ SplitOS compatibility validation
→ CompatibilityDecision
→ SplitOS release/update metadata
→ local Update Orchestration
→ controlled apply
→ verification
→ recovery/rollback if required
```

---

## 9.1 Windows Update agent boundary

Exact Windows Update Agent/servicing mechanism for production SplitOS updates is still OPEN.

Potential directions:

```text
standard Windows servicing APIs under SplitOS policy control
packaged cumulative updates integrated into tested SplitOS release
supported enterprise/update-management mechanisms
offline/maintenance application where necessary
```

Do not disable the platform's servicing capability so aggressively that SplitOS cannot safely maintain security/recovery.

---

# 10. SplitOS-owned update packages

SplitOS runtime/components themselves need a separate update path from Windows patch content.

Required properties:

```text
signed release metadata
package integrity verification
version compatibility
atomic or recoverable install semantics
rollback/recovery metadata
entitlement policy where applicable
```

Exact installer/package technology remains OPEN.

---

# 11. Update privilege boundary

Update operations that modify protected machine state should run through a trusted privileged update/broker path.

UI behavior:

```text
Manager
→ RequestUpdate
→ Update Orchestration
→ compatibility/entitlement checks
→ privileged apply mechanism
→ verify
→ result
```

Manager must not launch arbitrary elevated installers supplied by untrusted input.

---

# 12. Update verification

Immediate installer/process exit code is not enough.

Verify conceptually:

```text
installed SplitOS version
expected Windows baseline/version
required package/component state
service/runtime health
boot/restart completion where required
```

If reboot is required:

```text
update transaction remains non-terminal
until post-reboot verification
```

---

# 13. Recovery integration

Builder and Update layers must provide recovery-relevant artifacts.

Examples:

```text
last known supported release
rollback package/reference
baseline identity
update transaction journal
recovery assets
```

Recovery Coordinator consumes this evidence but remains owner of recovery target/result.

---

# 14. Account / payment / update failure separation

These failures must remain distinct:

```text
AUTH_BACKEND_UNAVAILABLE
ENTITLEMENT_STALE
PAYMENT_PROVIDER_UNAVAILABLE
CHECKOUT_CANCELLED
BUILD_SOURCE_UNSUPPORTED
BUILD_ACTION_FAILED
UPDATE_COMPATIBILITY_BLOCKED
UPDATE_APPLY_FAILED
UPDATE_VERIFY_FAILED
```

This distinction is required for later Failure analysis.

---

# 15. Official servicing / Windows references

```text
https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/enable-or-disable-windows-features-using-dism?view=windows-11
https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/add-or-remove-packages-offline-using-dism?view=windows-11
https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/preinstall-apps-using-dism?view=windows-11
https://learn.microsoft.com/en-us/windows/apps/develop/launch/handle-uri-activation
```

---

# 16. Integration decisions

### INT-ACC-001
Use backend API boundary for SplitOS account/entitlement; Windows account remains independent.

### INT-PAY-001
Prefer hosted checkout with backend-side validated provider evidence.

### INT-PAY-002
Desktop/browser callback only triggers reconciliation; it never directly grants entitlement.

### INT-BUILD-001
Use versioned manifest-driven offline servicing.

### INT-BUILD-002
Use DISM-supported operations where appropriate to component type.

### INT-BUILD-003
A failed mandatory build transformation invalidates supported-baseline result.

### INT-UPD-001
Separate Microsoft patch source, SplitOS compatibility decision and local update execution.

### INT-UPD-002
Post-reboot verification may be part of one durable update transaction.

---

# 17. Open decisions

- account authentication protocol;
- credential/token protection mechanism;
- payment provider;
- hosted checkout callback mechanism;
- Build Manifest physical schema;
- Windows-source acquisition UX and legal constraints;
- exact Windows patch orchestration technology;
- SplitOS package installer/update format;
- signature/public-key lifecycle;
- update channel model;
- offline entitlement validity interval;
- multi-device subscription/device limits.

---

# 18. Result

The integrations now preserve a clean ownership chain:

```text
Payment Provider
→ payment evidence
→ SplitOS Backend
→ entitlement
→ local runtime access

Microsoft Windows Source
→ Builder
→ manifest-driven servicing
→ verified baseline

Microsoft Update
→ compatibility validation
→ SplitOS update decision
→ controlled local apply
→ verified installed state
```