# SplitOS — Initial Provisioning and Package Delivery

## 1. Purpose

This document defines the implementable v1 contract for turning a cleanly installed prepared Windows baseline into a complete SplitOS release that is ready for normal First Run onboarding.

Canonical sequence:

```text
Prepared SplitOS installation media
↓
Windows Setup / OOBE
↓
first Windows user + first sign-in
↓
Initial Provisioning
↓
READY_FOR_FIRST_RUN
or READY_WITH_DEFERRED_OPTIONALS
↓
SplitOS First Run / Account / personalization
```

Core distinction:

```text
Windows deployment success
!=
SplitOS release provisioning success
```

---

## 2. Responsibility boundary

### Builder

Builder owns:

```text
release package-set validation
staging release-owned packages on installation media
staging approved third-party provisioning descriptors/assets where allowed
provisioning-plan identity
setup/bootstrap registration
build-time verification
```

Builder does not execute normal post-sign-in package provisioning against a future user's live session.

### Setup / Machine Bootstrap

Machine bootstrap owns the minimum installed-machine substrate needed before user provisioning can run, including release-defined machine components such as:

```text
Broker service installation/configuration
protected machine directories
InstalledBaselineIdentity seed
Runtime/Provisioning bootstrap registration
local release package store registration
```

### Initial Provisioning Coordinator

Initial Provisioning Coordinator owns the semantic post-install transaction:

```text
resolve plan
→ verify local release payload
→ install/verify required platform
→ install/verify first-party features
→ attempt approved third-party preparation
→ finalize readiness
```

In v1 this SHOULD be a module hosted by Runtime Host/setup bootstrap rather than a new permanent privileged executable.

### SplitOS Manager

Manager owns user-facing status/retry for deferred optional packages after first-run readiness.

Manager is not package-install authority by itself; it sends bounded provisioning intents to the owning coordinator.

---

## 3. Package classes

Every Initial Provisioning item MUST belong to one semantic class.

```text
REQUIRED_PLATFORM
FIRST_PARTY_BUNDLED
THIRD_PARTY_PROVISIONED
```

### 3.1 REQUIRED_PLATFORM

SplitOS-owned release package required to establish supported product substrate.

Typical examples:

```text
SplitOS.RuntimeHost
SplitOS.Broker.Service
SplitOS.Manager / First Run shell
persistence/bootstrap package
recovery/update bootstrap package
```

A release may install some of these during `specialize`; Initial Provisioning still verifies their required postconditions.

### 3.2 FIRST_PARTY_BUNDLED

SplitOS-owned feature package shipped as part of the release payload.

Current/future examples:

```text
SplitOS.GameLauncher
SplitOS.Audio.Equalizer
SplitOS.Input.Hotkeys
SplitOS.Widgets
SplitOS.Pins
SplitOS.ControllerTools
SplitOS.PerformanceTools
SplitOS.DisplayTools
```

A package may be entitlement-gated at runtime even though it is already installed.

```text
installed
!= authorized for active premium behavior
```

### 3.3 THIRD_PARTY_PROVISIONED

External software that SplitOS intentionally prepares as part of a recommended/conceptual environment.

Examples may include supported game clients or companion applications.

SplitOS does not become owner of:

```text
third-party account
license
store
update authority
cloud data
```

---

## 4. Release media completeness rule

For v1 every package with:

```text
class ∈ {REQUIRED_PLATFORM, FIRST_PARTY_BUNDLED}
and
requiredForFirstUse = true
```

MUST have its complete release artifact available from the prepared installation media/local staged package store.

The normal first-install path MUST NOT require:

```text
SplitOS CDN
SplitOS Account Backend
GitHub
community mirror
```

to acquire these release-owned binaries.

This does not mean every binary must be physically injected into `install.wim`.

Preferred model:

```text
installation media
└── SplitOS release payload
    ├── Platform/
    ├── FirstParty/
    ├── Provisioning/
    └── Recovery/

Windows Setup
↓
stage/register payload
↓
Initial Provisioning installs/verifies live packages
```

Exact media directory names are packaging details, not semantic identifiers.

---

## 5. ProvisioningPlan

A release MUST have a signed/versioned provisioning plan bound to the exact SplitOS release.

Conceptual object:

```text
ProvisioningPlan
├── schemaVersion
├── planId
├── splitOsReleaseId
├── buildManifestId/digest
├── packageCatalogId/digest
├── requiredItems[]
├── firstPartyItems[]
├── thirdPartyItems[]
├── finalizationPredicates[]
└── policyVersion
```

The plan is release-owned instruction data and cannot be modified by ordinary user settings into arbitrary execution.

---

## 6. ProvisioningPackage descriptor

Common semantic fields:

```text
packageId
packageClass
packageVersion
requiredForFirstUse
capabilityIds[]
sourceKind
artifactIdentity / adapterId
publisherConstraint
integrityConstraint
installHandlerId
verifyHandlerId
dependsOn[]
rebootBehavior
failurePolicy
```

For first-party bundled packages:

```text
sourceKind = LOCAL_RELEASE_BUNDLE
```

For external software allowed modes may include:

```text
MEDIA_BUNDLED_VENDOR_AUTHORIZED
OFFICIAL_VENDOR_ONLINE
SUPPORTED_STORE_ACQUISITION
```

No production descriptor may be equivalent to:

```text
url
commandLine
runAsAdmin
```

without a bounded release-owned adapter that interprets a typed package identity.

---

## 7. Initial provisioning state model

Durable transaction state MUST represent at least equivalent semantics to:

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

The implementation MAY normalize these into transaction + item states, but must preserve the distinction between:

```text
required failure
optional defer
successful first-use readiness
```

---

## 8. Provisioning item state

Per-item durable status SHOULD support:

```text
PENDING
RESOLVING
ACQUIRING
VERIFYING_ARTIFACT
INSTALLING
VERIFYING_INSTALL
VERIFIED
DEFERRED
FAILED_REQUIRED
FAILED_RETRYABLE
SKIPPED_POLICY
```

Persisted item status is orchestration evidence only.

```text
status = VERIFIED
```

must be backed by actual installed-state verification whenever the coordinator resumes after crash/reboot.

---

## 9. Stage 1 — platform verification/preparation

Before feature provisioning, Coordinator verifies the installed substrate:

```text
InstalledBaselineIdentity present
expected release package store available
Broker installed/configured where required
Runtime/Provisioning bootstrap can start
machine data/ACL baseline exists
mandatory recovery/bootstrap assets available
```

Missing required substrate:

```text
→ attempt bounded repair if release contract permits
→ verify
→ otherwise BLOCKED_REQUIRED_FAILURE / RECOVERY_REQUIRED
```

---

## 10. Stage 2 — first-party package installation

For each `FIRST_PARTY_BUNDLED` item:

```text
resolve local artifact by packageId/version
↓
verify release binding + digest/signature/publisher as applicable
↓
check dependency predicates
↓
install/register with typed handler
↓
read actual installed state
↓
verify expected version/capabilities
↓
VERIFIED
```

The handler contract is package-type-specific and MUST NOT expose a generic privileged command surface to UI/backend data.

Potential package types may include:

```text
SplitOS desktop executable package
Windows service package
WinUI/application package
release-owned data/knowledge package
future driver-bound package with separate security/compatibility gate
```

Driver-bound features are not implicitly authorized by being first-party; their own signing/compatibility requirements still apply.

---

## 11. Stage 3 — third-party provisioning

### 11.1 Ownership

Third-party software remains external authority.

SplitOS provisions only software explicitly present in the release-approved third-party catalog.

### 11.2 Acquisition

Adapter resolves a stable SplitOS-owned software identifier:

```text
THIRDPARTY.STEAM
```

into a bounded supported mechanism for that release/catalog version.

Examples:

```text
vendor-authorized bundled installer
official vendor URL/acquisition API
supported Microsoft Store package acquisition
```

The exact supported mechanism is product/vendor-specific and must be validated before a package is marked provisionable.

### 11.3 Network unavailable

If item requires online acquisition:

```text
network unavailable
→ DEFERRED_NETWORK
```

No indefinite blocking of first-use readiness.

### 11.4 External installer failure

A non-required external package may converge to:

```text
FAILED_RETRYABLE
or DEFERRED
```

and be surfaced later in Manager.

Third-party package MUST NOT be classified `REQUIRED_PLATFORM`.

---

## 12. Readiness predicates

`READY_FOR_FIRST_RUN` requires all release-defined mandatory predicates.

Minimum conceptual predicates:

```text
platformBaselineVerified
allRequiredPlatformPackagesVerified
allRequiredFirstPartyPackagesVerified
provisioningJournalDurable
Manager/FirstRun launch path available
no required recovery/provisioning failure outstanding
```

If only optional/recommended items remain incomplete:

```text
READY_WITH_DEFERRED_OPTIONALS
```

is allowed.

---

## 13. Offline behavior

Supported offline clean install MUST allow:

```text
Windows Setup
↓
platform preparation
↓
all required bundled first-party package installation
↓
READY_WITH_DEFERRED_OPTIONALS
```

when third-party online acquisition/account services are unavailable.

Account/backend authentication occurs later in FL-01 and cannot be used as a gate for local first-party package acquisition.

---

## 14. Crash/reboot/reconciliation

Initial Provisioning is a durable operation.

After crash/reboot:

```text
load ProvisioningTransaction
↓
read actual installed state for in-flight/recent items
↓
compare expected release package state
↓
resume or repair
```

Example:

```text
item journal = INSTALLING
process died
↓
read actual package registration/version
├── expected installed → verify → VERIFIED
└── absent/partial → bounded retry/repair
```

Blindly executing the installer twice without read-back is forbidden.

---

## 15. Reboot requests

Packages MAY declare bounded reboot behavior:

```text
NO_REBOOT_EXPECTED
REBOOT_ALLOWED
REBOOT_REQUIRED
```

Coordinator must persist a resume checkpoint before accepting a required reboot.

After reboot it re-enters reconciliation; it does not infer success from reboot completion.

---

## 16. User-facing provisioning surface

Initial Provisioning UI should present semantic package groups/stages, not raw installer mechanics.

Example:

```text
Preparing SplitOS

Core Platform          Ready
SplitOS Features       Installing
Gaming & Apps          Waiting for network
Finalizing             Pending
```

Required failures offer bounded retry/repair/recovery choices.

Optional/deferred software allows continuation once core readiness is satisfied.

---

## 17. Deferred setup in Manager

Manager receives a projection such as:

```text
DeferredProvisioningItem
├── packageId
├── display metadata
├── reasonCode
├── lastAttemptAt
├── retryAllowed
└── userActionRequired
```

Manager may request:

```text
RetryProvisioning(packageId)
DeclineOptional(packageId)
OpenVendorAction(packageId) where required
```

Manager does not receive arbitrary installer commands.

---

## 18. Security and privilege

Privileged operations follow existing SplitOS boundaries:

```text
Provisioning Coordinator
→ typed semantic operation
→ Broker where privilege required
```

The Broker MUST NOT gain a generic package-execution capability.

Allowed future capability shape is package/catalog bound, for example:

```text
Provisioning.Package.InstallVerified@1
```

where Broker independently verifies allowed package identity/release context rather than receiving arbitrary path/arguments.

Exact privileged package install catalog is implementation/security work and must preserve SPEC-02/SPEC-12 trust rules.

---

## 19. Relation to entitlement

Provisioning and entitlement are orthogonal:

```text
package installed
!= capability authorized
```

A PRO-only first-party feature MAY be bundled and installed for every supported machine so FREE→PRO does not require product reinstall.

Runtime Access/Entitlement owner still decides whether its premium behavior is available.

---

## 20. Relation to updates

Initial Provisioning installs the release present on installation media.

It MUST NOT silently redefine installed release identity to a newer CDN release during setup.

After first-use readiness, SPEC-11 may discover/update to a newer SplitOS release through the normal signed update transaction.

Optional knowledge refresh may occur later according to product policy, but local required first-party readiness remains based on the installed release.

---

## 21. Verification backlog

At minimum SPEC-14/implementation tests must cover:

- complete offline first-party provisioning;
- network unavailable with online third-party items;
- required platform artifact missing/corrupt;
- required first-party artifact hash mismatch;
- first-party installer technical success but read-back mismatch;
- third-party vendor endpoint unavailable;
- third-party publisher mismatch;
- crash during first-party install;
- reboot during provisioning;
- duplicate/resume idempotency;
- deferred optional retry through Manager;
- no account backend while bundled provisioning succeeds;
- arbitrary remote command/URL cannot become privileged install authority;
- First Run does not begin before mandatory readiness.

---

## 22. Traceability

```text
DEC-048..051
→ FR-PROV-*
→ FL-00 Initial Provisioning
→ SPEC-10 Build Manifest / Installation Media / this contract
→ Grooming provisioning epic/slice
→ SPEC-14 verification evidence
```
