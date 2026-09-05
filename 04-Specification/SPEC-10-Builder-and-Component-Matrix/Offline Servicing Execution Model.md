# SplitOS — Offline Servicing Execution Model

## 1. Purpose

This document maps typed Build Manifest operations onto supported Windows image servicing stages without turning the Builder into a generic shell runner.

## 2. Primary v1 servicing substrate

The supported v1 baseline uses Microsoft deployment/servicing mechanisms centered on:

```text
Windows ADK / DISM
mounted offline install image
Windows Setup answer-file configuration passes
approved setup/bootstrap provisioning
```

The Builder wraps these mechanisms behind typed adapters.

## 3. Image lifecycle

```text
validated immutable source
↓
copy/extract to working area
↓
resolve install image + selected index
↓
mount image
↓
collect pre-mutation inventory
↓
apply manifest phases
↓
run offline verification
↓
commit mount
↓
assemble installation media/deployment source
↓
run output verification
```

On any required failure before commit:

```text
DISCARD mount
↓
mark build failed
```

v1 MUST prefer discard/rebuild over trying to repair an unknown partially serviced offline mount after executor failure.

## 4. DISM package servicing

`REMOVE_OS_PACKAGE` maps only to packages explicitly identified and accepted by release knowledge.

The executor MUST:

1. inventory exact package identity;
2. verify source-build match;
3. verify Component Matrix state = accepted `REMOVE`;
4. execute supported DISM package removal;
5. query inventory again;
6. confirm required postcondition.

A removal command returning success without absence/read-back is insufficient.

## 5. Provisioned AppX servicing

`REMOVE_PROVISIONED_APPX` applies to provisioned packages that Windows supports removing from the offline image.

Semantics:

```text
provisioned package present
↓
remove provisioning from offline image
↓
new Windows users do not receive the package
```

The operation MUST NOT be generated for packages marked non-removable/unsupported by release verification.

System apps and Microsoft Store are not assumed removable merely because they appear in AppX inventories.

## 6. Optional features

`DISABLE_OPTIONAL_FEATURE` maps to supported Windows feature servicing.

SplitOS distinguishes:

```text
DISABLED
```

from:

```text
payload physically removed
```

The Builder MUST verify the actual Windows client behavior for the target release. It MUST NOT claim image-size removal when Windows retains payload for reset/repair semantics.

If a feature reaches a pending state requiring boot completion, that operation cannot be considered fully verified offline unless the release verification profile explicitly includes a boot-phase validation step.

## 7. Drivers

`ADD_APPROVED_DRIVER_PACKAGE` is allowed only for release-owned, explicitly approved driver packages.

Rules:

- arbitrary user drivers are not part of the canonical release baseline;
- boot-critical driver strategy is separate from ordinary driver staging;
- every added driver is digest/version/provider identified;
- driver removal is high-risk and not a generic optimization operation;
- removing a driver that may be required for boot is forbidden without dedicated release evidence.

Hardware-specific end-user drivers SHOULD normally be handled after deployment by Windows/OEM/vendor mechanisms unless SplitOS has an explicit reason to own them.

## 8. Service baseline mutations

DISM is not treated as a generic service-policy API.

`SET_MANAGED_SERVICE_BASELINE` uses a typed release adapter that resolves:

```text
ManagedServiceId
→ exact service identity for supported Windows build
→ allowed startup baseline
→ dependency constraints
```

No manifest field carries a free-form service name.

The offline implementation MAY use supported offline registry/configuration techniques internally, but arbitrary registry edits are never exposed as manifest semantics.

## 9. Baseline policy mutations

`APPLY_BASELINE_POLICY` uses release-owned `policyId` values.

Examples of semantic policy families:

```text
consumer-content policy
notification/promotional baseline
privacy-related baseline
setup/OOBE policy
SplitOS runtime bootstrap policy
```

Each policy implementation is separately version-gated to Windows build/edition.

## 10. SplitOS package staging

Microsoft documentation distinguishes modifying image files from installing arbitrary desktop applications into a mounted image as if Windows were running.

Therefore SplitOS-owned runtime payload SHOULD follow:

```text
verify signed/digest-bound package
↓
stage trusted files/package into image/media
↓
register trusted setup provisioning
↓
install/register during supported Setup/specialize/bootstrap phase
↓
verify installed SplitOS runtime after deployment
```

The exact package technology may differ by component, but offline file copy alone is not equivalent to successful installed runtime provisioning.

## 11. Answer-file/configuration-pass model

SplitOS MAY use release-owned answer-file templates across supported Setup phases.

Primary semantics:

```text
offlineServicing
→ offline image settings/packages/drivers supported in that pass

specialize
→ machine-specific installed-system configuration/bootstrap

oobeSystem
→ only user/OOBE-facing configuration genuinely required there
```

`oobeSystem` SHOULD be used sparingly; SplitOS Account authentication remains after Windows user creation/first sign-in per SPEC-04.

## 12. No hidden audit-mode dependency for consumer installation

Audit mode MAY be used in release engineering/test image preparation, but ordinary end-user SplitOS installation SHOULD NOT require an exposed/manual Audit Mode flow.

Any production bootstrap must converge automatically to standard Windows OOBE and a usable user session.

## 13. Component-store cleanup

Aggressive component-store cleanup is not a default product operation.

`StartComponentCleanup` / `ResetBase` or equivalent cleanup MAY be evaluated in release engineering, but irreversible servicing consequences require explicit validation.

The Build Manifest MUST NOT include an unqualified generic "clean WinSxS" operation.

## 14. Media assembly

After install-image commit, Builder assembles a deployment source containing:

```text
Windows setup/boot structure
transformed install image
release-owned setup/unattend assets
SplitOS staged package assets
required recovery/bootstrap assets
build identity metadata
```

The output format MAY be:

```text
bootable USB preparation
ISO/deployment tree where legally/product-operationally appropriate
```

Public redistribution of Microsoft binaries is outside this specification and remains subject to the product's legal/source model.

## 15. Logging

Every servicing adapter invocation records:

```text
operationId
componentId
adapter/mechanism
tool version
start/end time
exit/result code
precondition evidence
postcondition evidence
log artifact reference
```

Diagnostic logs support verification but do not replace postcondition state checks.
