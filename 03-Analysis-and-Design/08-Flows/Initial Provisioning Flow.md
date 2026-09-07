# SplitOS — Initial Provisioning Flow

## 1. Purpose

Документ определяет end-to-end flow между завершением Windows OOBE и обычным SplitOS First Run onboarding.

Canonical distinction:

```text
Windows deployed
!= SplitOS provisioning ready
!= SplitOS account onboarding complete
```

Initial Provisioning подготавливает installed release так, чтобы пользователь не получал голую Windows и не был обязан вручную устанавливать release-required SplitOS software после clean install.

---

## 2. Flow identity

```text
FL-00 Initial Provisioning
```

FL-00 выполняется до `FL-01 First Run / FREE-PRO / Upgrade` для новой clean installation.

---

## 3. Participants

```text
User
Windows Setup / Windows OOBE
SplitOS Machine Bootstrap
SplitOS Runtime Host
Initial Provisioning Coordinator
Provisioning Package Catalog
Local Release Package Store
Approved Third-Party Provisioning Adapters
SplitOS Manager / Provisioning UI
Observability & Diagnostics
Recovery Coordination
```

`Initial Provisioning Coordinator` является semantic responsibility. В v1 он MAY быть модулем Runtime Host / setup bootstrap и не требует отдельного permanent executable.

---

## 4. Preconditions

FL-00 начинается только когда:

- Windows deployment завершён;
- Windows OOBE позволил создать supported Windows user;
- first Windows sign-in произошёл;
- machine-level SplitOS bootstrap, необходимый для запуска provisioning coordinator, доступен либо его failure уже классифицирован;
- release identity / package catalog / staged package evidence доступны локально.

Account backend не является precondition для installation of bundled first-party release packages.

---

## 5. Package classes

Provisioning Coordinator работает с release-owned catalog, который различает:

```text
REQUIRED_PLATFORM
FIRST_PARTY_BUNDLED
THIRD_PARTY_PROVISIONED
```

### REQUIRED_PLATFORM

SplitOS-owned platform components, без которых нельзя подтвердить supported product readiness.

Examples:

```text
Runtime Host
Privileged Broker
required bootstrap/persistence/recovery components
Manager shell required for First Run
```

### FIRST_PARTY_BUNDLED

SplitOS-owned feature packages конкретного release.

Examples may include current/future:

```text
Game Launcher
Audio / Equalizer
Hotkeys
Widgets
Pins
Controller tools
Performance tools
Display tools
```

Requiredness задаётся release policy per package.

### THIRD_PARTY_PROVISIONED

External software, которое SplitOS может автоматически подготовить как часть recommended/conceptual environment.

Examples may include supported game clients or companion applications.

External vendor/platform остаётся authority для:

```text
license
account/auth
store
updates
cloud state
```

---

## 6. Durable provisioning lifecycle

Canonical semantic states:

```text
NOT_STARTED
PLATFORM_PREPARING
PLATFORM_VERIFYING
FIRST_PARTY_INSTALLING
FIRST_PARTY_VERIFYING
THIRD_PARTY_PROVISIONING
FINALIZING
READY_FOR_FIRST_RUN
READY_WITH_DEFERRED_OPTIONALS
BLOCKED_REQUIRED_FAILURE
RECOVERY_REQUIRED
```

Это не обязательно один DB enum в реализации, но физическая модель должна сохранять эквивалентную recoverable truth.

---

## 7. Main path

### 7.1 Start

1. First Windows sign-in completes.
2. Runtime/bootstrap reads InstalledBaselineIdentity and release provisioning plan.
3. Provisioning Coordinator loads existing durable provisioning transaction/journal or creates a new one.
4. Coordinator verifies that the plan belongs to the installed release.

### 7.2 REQUIRED_PLATFORM

1. Coordinator resolves required platform packages/postconditions.
2. Locally staged artifacts are integrity/provenance checked.
3. Missing/not-yet-installed required platform items are installed through typed package handlers.
4. Actual installed state is read back.
5. Required postconditions are verified.

If a required platform invariant cannot be verified:

```text
BLOCKED_REQUIRED_FAILURE
or RECOVERY_REQUIRED
```

FL-01 does not start.

### 7.3 FIRST_PARTY_BUNDLED

1. Coordinator loads `FIRST_PARTY_BUNDLED` items from local release package store.
2. Each package is checked against release identity/digest/trust evidence.
3. Package is installed/registered through its typed handler.
4. Actual installed version/capability evidence is read back.
5. Required first-party packages must verify.
6. Optional first-party packages follow release-defined failure policy, but v1 release SHOULD bundle the complete first-party feature set intended to be available immediately after initialization.

Network outage cannot be used as the reason why a release-owned bundled package cannot be acquired.

### 7.4 THIRD_PARTY_PROVISIONED

1. Coordinator resolves the release-approved recommended third-party set.
2. For each item it resolves an approved acquisition mode, for example:

```text
MEDIA_BUNDLED_VENDOR_AUTHORIZED
OFFICIAL_VENDOR_ONLINE
SUPPORTED_STORE_ACQUISITION
```

3. Coordinator validates package/publisher/acquisition evidence according to the adapter contract.
4. Installation is attempted with bounded typed semantics.
5. Result is recorded per item.

Third-party result may be:

```text
INSTALLED_VERIFIED
DEFERRED_NETWORK
DEFERRED_VENDOR_UNAVAILABLE
DECLINED
FAILED_RETRYABLE
UNSUPPORTED_CURRENT_CONTEXT
```

A non-critical third-party failure does not make core SplitOS installation false.

### 7.5 Finalization

Coordinator verifies:

```text
all mandatory platform predicates
all required first-party predicates
baseline identity consistency
required provisioning journal durability
Manager/First Run launch readiness
```

Then:

```text
no deferred optional items
→ READY_FOR_FIRST_RUN

optional/recommended items deferred
→ READY_WITH_DEFERRED_OPTIONALS
```

Only after one of these readiness results may ordinary SplitOS First Run account/personalization flow start.

---

## 8. Offline path

If network is unavailable:

```text
REQUIRED_PLATFORM
→ must remain installable from release media/local staged store

FIRST_PARTY_BUNDLED
→ must remain installable from release media/local staged store

THIRD_PARTY_PROVISIONED requiring online vendor acquisition
→ DEFERRED_NETWORK
```

Expected result:

```text
READY_WITH_DEFERRED_OPTIONALS
```

provided all mandatory SplitOS-owned predicates pass.

---

## 9. Crash / reboot / idempotency

Provisioning is not assumed to be one uninterrupted process lifetime.

After Runtime/Coordinator crash or reboot:

```text
read durable provisioning transaction
↓
read actual package/install state
↓
reconcile completed vs pending items
↓
resume from safe boundary
```

Persisted `INSTALLING` alone is not proof that package installation completed.

Every resumed mutating item must verify actual current state before retry.

```text
journal says APPLIED
!= actual package verified
```

---

## 10. Security boundary

Provisioning catalog is release-owned instruction data, not arbitrary remote code authority.

Forbidden conceptual contract:

```text
Download(url)
Run(commandLine, asAdmin=true)
```

from unbounded UI/backend metadata.

Third-party adapters must resolve bounded release-owned identifiers to supported acquisition/install mechanisms.

Privileged machine operations continue to use normal Broker/security boundaries where needed.

---

## 11. User-visible behavior

Provisioning UI SHOULD expose stage-level progress rather than raw installer output.

Example semantic view:

```text
Preparing SplitOS

Core Platform        VERIFIED
SplitOS Features     INSTALLING
Gaming Clients       WAITING_FOR_NETWORK
Finalizing           PENDING
```

A required failure must show a repair/retry/recovery outcome.

A deferred optional item must not trap the user indefinitely on the provisioning screen.

After `READY_WITH_DEFERRED_OPTIONALS`, SplitOS Manager owns the normal user-facing retry/complete-setup surface.

---

## 12. Handoff to FL-01

FL-00 result becomes precondition evidence for `First Run and Subscription Flow.md`.

```text
ProvisioningReadiness
∈ {READY_FOR_FIRST_RUN, READY_WITH_DEFERRED_OPTIONALS}
↓
FL-01 First Run
↓
SplitOS Account association
↓
Entitlement
↓
FREE or PRO
```

Account/entitlement is deliberately after mandatory machine/software preparation.

---

## 13. Invariants

### FL-PROV-001

`Windows sign-in succeeded != SplitOS Initial Provisioning completed`.

### FL-PROV-002

Bundled first-party release installation does not require SplitOS Account/backend availability.

### FL-PROV-003

Required SplitOS-owned package failure cannot be hidden as provisioning success.

### FL-PROV-004

Third-party provisioning failure cannot falsely mark Windows/core SplitOS unusable.

### FL-PROV-005

Remote/vendor metadata is evidence/input to a bounded adapter, not authority to execute arbitrary privileged command.

### FL-PROV-006

Provisioning journal is evidence of execution progress; actual installed package state must be verified.

---

## 14. Traceability

```text
DEC-048..051
→ FR-PROV-*
→ FL-00 Initial Provisioning
→ SPEC-10 Initial Provisioning and Package Delivery
→ Grooming provisioning backlog
```
