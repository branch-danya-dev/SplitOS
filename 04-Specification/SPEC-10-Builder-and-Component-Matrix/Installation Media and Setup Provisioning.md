# SplitOS — Installation Media and Setup Provisioning

## 1. Purpose

This document defines how a verified transformed Windows image becomes an installable SplitOS baseline and how the installed machine reaches first-use readiness without mixing Windows Setup, Initial Provisioning, SplitOS Account authentication and runtime mode selection.

Canonical distinction:

```text
Windows deployed
!= SplitOS provisioned
!= SplitOS First Run/account complete
```

---

## 2. Separation of responsibilities

```text
Builder
→ prepares deployment source/media
→ stages complete required SplitOS release payload
→ binds Initial Provisioning plan/catalog

Windows Setup
→ installs Windows baseline

SplitOS setup/bootstrap
→ establishes minimum machine substrate
→ registers Initial Provisioning entry path

Windows OOBE / user
→ creates Windows user
→ first Windows sign-in

SplitOS Initial Provisioning
→ verifies/installs REQUIRED_PLATFORM
→ verifies/installs FIRST_PARTY_BUNDLED
→ attempts approved THIRD_PARTY_PROVISIONED
→ publishes READY_FOR_FIRST_RUN or READY_WITH_DEFERRED_OPTIONALS

SplitOS First Run
→ associates SplitOS Account
→ resolves FREE / PRO
→ gathers personalization/mode setup where applicable
```

SplitOS Account login MUST NOT be required to complete Windows disk deployment, Windows user creation or bundled first-party provisioning.

---

## 3. Media contents

A supported prepared deployment source contains at least:

```text
Windows setup/boot files
verified transformed install image
release-owned unattended setup assets
complete required SplitOS platform package payload
complete required first-party feature package payload
Initial Provisioning plan/catalog
approved third-party provisioning descriptors/assets where redistribution is authorized
baseline identity descriptor
recovery/update bootstrap assets
```

The media MUST contain every release-owned artifact required for:

```text
REQUIRED_PLATFORM
+
FIRST_PARTY_BUNDLED where requiredForFirstUse=true
```

so that initial first-party provisioning does not depend on downloading SplitOS binaries from CDN/backend.

The exact ISO/USB directory layout follows Windows Setup requirements and release verification.

---

## 4. Setup passes

SplitOS MAY use Windows Setup configuration passes only for behavior assigned to those phases.

### windowsPE

Use only where needed for installer/preinstallation behavior such as storage/deployment setup or boot-critical driver needs.

### offlineServicing

Use for supported offline packages/drivers/settings that belong to that pass.

### specialize

Preferred installed-machine phase for trusted machine bootstrap that must exist before ordinary user provisioning/runtime.

Potential responsibilities:

```text
install/configure SplitOSBroker service
establish protected machine data directories
stage/register RuntimeHost/Provisioning bootstrap
register local release package store
write InstalledBaselineIdentity seed
stage required provisioning/recovery metadata
perform machine-bootstrap verification
```

`specialize` does NOT need to install every first-party feature package. It establishes the trusted substrate from which Initial Provisioning can install/verify the complete release package set after the Windows user exists.

### oobeSystem

Use sparingly for Windows OOBE presentation/configuration only.

SplitOS MUST NOT replace normal Windows identity semantics with SplitOS Account identity.

---

## 5. First sign-in and Initial Provisioning bootstrap

After a Windows user exists and signs in, the installed machine launches the per-session Runtime/Provisioning bootstrap using the mechanism specified in SPEC-01 / release provisioning contract.

For a new clean installation the sequence is:

```text
Windows user context
→ Runtime/Provisioning bootstrap
→ load InstalledBaselineIdentity + ProvisioningPlan
→ FL-00 Initial Provisioning
→ READY_FOR_FIRST_RUN or READY_WITH_DEFERRED_OPTIONALS
→ SplitOS First Run
→ Account/Auth (SPEC-04)
→ FREE or PRO runtime access
```

The Runtime MUST NOT skip directly to normal First Run merely because `RuntimeHost.exe` itself can start.

---

## 6. Initial Provisioning package classes

Provisioning uses:

```text
REQUIRED_PLATFORM
FIRST_PARTY_BUNDLED
THIRD_PARTY_PROVISIONED
```

Detailed contract lives in:

```text
Initial Provisioning and Package Delivery.md
```

The installation media owns/stages first-party release payload. External third-party software remains external vendor/platform authority even when SplitOS prepares it automatically.

---

## 7. Offline first-use behavior

A supported installation without network access MUST still be able to establish required SplitOS-owned software readiness from local media/staged release payload.

Expected offline path:

```text
Windows install
↓
REQUIRED_PLATFORM verified
↓
FIRST_PARTY_BUNDLED verified
↓
online-only third-party items deferred
↓
READY_WITH_DEFERRED_OPTIONALS
↓
First Run may continue
```

Network/backend outage MUST NOT force the user to download core SplitOS binaries before the product can initialize.

---

## 8. Third-party provisioning

External software may be part of the recommended SplitOS first-use environment but MUST use a release-approved acquisition/distribution path.

Possible validated modes:

```text
MEDIA_BUNDLED_VENDOR_AUTHORIZED
OFFICIAL_VENDOR_ONLINE
SUPPORTED_STORE_ACQUISITION
```

If acquisition requires network/vendor infrastructure and that dependency is unavailable:

```text
third-party item → DEFERRED
core readiness → may continue
```

SplitOS MUST NOT silently use unofficial mirrors or generic arbitrary download-and-run scripts as a product dependency.

---

## 9. FREE installation outcome

A valid SplitOS installation with no paid entitlement still converges to:

```text
Initial Provisioning = ready
Windows desktop usable
SplitOS Runtime Host available
SplitOS Manager available
release-required first-party packages installed/verified
ManagedRuntime = DISABLED
OperationalMode = NONE
```

No Builder/setup/provisioning phase may make Windows usability depend on successful subscription purchase.

Entitlement may limit active behavior of already installed premium packages.

```text
package installed
!= premium capability authorized
```

---

## 10. PRO installation outcome

If the user obtains PRO after provisioning:

```text
entitlement refresh
↓
ManagedRuntime enabled
↓
mode/personalization setup if needed
↓
ACTIVATE WORK or GAME
```

No Windows reinstall, Builder rerun or repeat first-party package download is required merely to unlock PRO when the package belongs to the installed release.

---

## 11. Destructive disk disclosure

Before any installer flow erases/formats a selected system disk, the user MUST receive explicit destructive-operation disclosure.

Before that destructive point the product MUST also make material account/entitlement information available, including that:

```text
SplitOS Account is separate from Windows identity
FREE/base use remains available
paid entitlement unlocks managed/premium capabilities
```

The user must not first discover a major paid limitation after the previous system has already been erased.

---

## 12. Target disk selection

Target-disk selection is installer authority, not Build Manifest free-form behavior.

The Builder may prepare bootable media, but it MUST NOT bake a specific user's disk identifier into a reusable release manifest.

Installer safety should distinguish:

```text
installation media device
selected target system disk
other attached disks
```

Automatic destructive behavior without explicit user intent is forbidden for v1.

---

## 13. Provisioning verification

Machine bootstrap + Initial Provisioning MUST verify the applicable predicates rather than relying on installer exit codes.

At minimum:

```text
SplitOSBroker service installed/configured where required
Runtime/Provisioning bootstrap package matches release
local package store/plan/catalog match release
machine data directories/ACLs established
baseline identity available
all required platform packages verified
all required first-party packages verified
Manager/First Run path available
no fatal provisioning/recovery result outstanding
```

Failure produces typed provisioning/repair/recovery behavior, not fake successful initialization.

Optional/recommended third-party failures are separately represented and can produce `READY_WITH_DEFERRED_OPTIONALS`.

---

## 14. Provisioning crash/reboot behavior

Initial Provisioning MUST be resumable/reconcilable.

```text
crash / reboot
↓
read durable provisioning journal
↓
read actual package/service/registration state
↓
verify
↓
resume / retry / repair
```

Persisted progress alone is not proof of installation success.

A package that requires reboot must persist a safe resume checkpoint before restart.

---

## 15. Windows activation/license

Windows Setup/activation remains Microsoft-owned behavior.

SplitOS does not bypass or substitute Windows activation.

---

## 16. Recovery/bootstrap assets

SPEC-10 stages release-defined recovery assets.

SPEC-11 defines how installed recovery/update transactions use them.

A Builder output MUST NOT be considered fully verified if mandatory recovery/bootstrap/provisioning assets defined by the manifest are missing.

---

## 17. OEM/vendor driver handling

The generic SplitOS media SHOULD avoid embedding arbitrary hardware-specific driver collections into the canonical baseline.

Exceptions may include:

```text
required installer/storage/network boot drivers
SplitOS-owned/supporting driver packages
release-approved compatibility-critical drivers
```

Such drivers require exact release metadata and validation.

First-party feature ownership does not automatically authorize a kernel/driver package; driver-bound features require their own signing/security/compatibility evidence.

---

## 18. Post-install update bootstrap

A fresh installation MAY discover newer SplitOS or Windows updates after first-use readiness, but the installed baseline identity remains the version actually installed until SPEC-11 update verification commits a new identity.

```text
new update available
!=
installed baseline already updated
```

Initial Provisioning MUST NOT silently replace the release on installation media with a newer CDN release as part of its required first-use path.

---

## 19. Supported clean-install model

v1 product support targets:

```text
known prepared baseline/media
→ clean Windows installation
→ Initial Provisioning from exact release payload
→ First Run
```

Mutation of an arbitrary existing Windows installation into SplitOS is not a supported equivalent path unless a future dedicated migration specification is created.
