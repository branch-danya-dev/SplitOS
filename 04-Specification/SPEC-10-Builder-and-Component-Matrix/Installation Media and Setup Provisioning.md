# SplitOS — Installation Media and Setup Provisioning

## 1. Purpose

This document defines how a verified transformed Windows image becomes an installable SplitOS baseline without mixing Windows Setup, SplitOS Account authentication and runtime mode selection.

## 2. Separation of responsibilities

```text
Builder
→ prepares deployment source/media

Windows Setup
→ installs Windows baseline

SplitOS setup/bootstrap
→ provisions SplitOS machine runtime

Windows user/OOBE
→ creates/signs in Windows user

SplitOS First Run
→ associates SplitOS Account
→ resolves FREE / PRO
```

SplitOS Account login MUST NOT be required to complete Windows disk deployment or Windows user creation.

## 3. Media contents

A supported prepared deployment source contains at least:

```text
Windows setup/boot files
verified transformed install image
release-owned unattended setup assets
SplitOS staged packages/bootstrap assets
baseline identity descriptor
recovery/update bootstrap assets
```

The exact ISO/USB layout follows Windows Setup requirements and release verification.

## 4. Setup passes

SplitOS MAY use Windows Setup configuration passes only for behavior assigned to those phases.

### windowsPE

Use only where needed for installer/preinstallation behavior such as storage/deployment setup or boot-critical driver needs.

### offlineServicing

Use for supported offline packages/drivers/settings that belong to that pass.

### specialize

Preferred installed-machine phase for trusted machine bootstrap that must exist before ordinary user runtime.

Potential responsibilities:

```text
register/install SplitOS machine runtime packages
install/configure SplitOSBroker service
establish protected machine data directories
stage scheduled/logon RuntimeHost bootstrap
write InstalledBaselineIdentity seed
perform machine provisioning verification
```

### oobeSystem

Use sparingly for Windows OOBE presentation/configuration only.

SplitOS MUST NOT replace normal Windows identity semantics with SplitOS Account identity.

## 5. Runtime Host bootstrap

After a Windows user exists and signs in, the installed machine launches the per-session Runtime Host using the mechanism specified in SPEC-01.

This begins:

```text
Windows user context
→ Runtime Host
→ SplitOS First Run if association missing
→ Account/Auth (SPEC-04)
→ FREE or PRO runtime access
```

## 6. FREE installation outcome

A valid SplitOS installation with no paid entitlement still converges to:

```text
Windows desktop usable
SplitOS Runtime Host available
SplitOS Manager available
ManagedRuntime = DISABLED
OperationalMode = NONE
```

No Builder/setup phase may make Windows usability depend on successful subscription purchase.

## 7. PRO installation outcome

If the user later obtains PRO:

```text
entitlement refresh
↓
ManagedRuntime enabled
↓
mode selection
↓
ACTIVATE WORK or GAME
```

No Windows reinstall or Builder rerun is required merely to unlock PRO runtime capabilities.

## 8. Destructive disk disclosure

Before any installer flow erases/formats a selected system disk, the user MUST receive explicit destructive-operation disclosure.

Before that destructive point the product MUST also make material account/entitlement information available, including that:

```text
SplitOS Account is separate from Windows identity
FREE/base use remains available
paid entitlement unlocks managed Work/Game capabilities
```

The user must not first discover a major paid limitation after the previous system has already been erased.

## 9. Target disk selection

Target-disk selection is installer authority, not Build Manifest free-form behavior.

The Builder may prepare bootable media, but it MUST NOT bake a specific user's disk identifier into a reusable release manifest.

Installer safety should distinguish:

```text
installation media device
selected target system disk
other attached disks
```

Automatic destructive behavior without explicit user intent is forbidden for v1.

## 10. First boot provisioning verification

Machine bootstrap MUST verify at least:

```text
SplitOSBroker service installed/configured
RuntimeHost/Manager/Launcher package versions match release
machine data directories/ACLs established
baseline identity available
required machine configuration present
no fatal provisioning result outstanding
```

Failure produces typed provisioning/recovery behavior, not a fake successful SplitOS installation.

## 11. Windows activation/license

Windows Setup/activation remains Microsoft-owned behavior.

SplitOS does not bypass or substitute Windows activation.

## 12. Recovery/bootstrap assets

SPEC-10 only stages release-defined recovery assets.

SPEC-11 defines how installed recovery/update transactions use them.

A Builder output MUST NOT be considered fully verified if mandatory recovery/bootstrap files defined by the manifest are missing.

## 13. OEM/vendor driver handling

The generic SplitOS media SHOULD avoid embedding arbitrary hardware-specific driver collections into the canonical baseline.

Exceptions may include:

```text
required installer/storage/network boot drivers
SplitOS-owned/supporting driver packages
release-approved compatibility-critical drivers
```

Such drivers require exact release metadata and validation.

## 14. Post-install update bootstrap

A fresh installation MAY discover newer SplitOS or Windows updates after first boot, but the installed baseline identity remains the version actually installed until SPEC-11 update verification commits a new identity.

```text
new update available
!=
installed baseline already updated
```

## 15. Supported clean-install model

v1 product support targets:

```text
known prepared baseline
→ clean installation
```

Mutation of an arbitrary existing Windows installation into SplitOS is not a supported equivalent path unless a future dedicated migration specification is created.
