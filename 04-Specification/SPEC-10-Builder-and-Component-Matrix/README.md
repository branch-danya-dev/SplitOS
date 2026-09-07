# SPEC-10 — Builder & Windows Component Matrix

## Status

`READY FOR REVIEW`

## Purpose

SPEC-10 turns the build-time architecture into an implementable contract for producing a supported SplitOS Windows baseline from a validated Microsoft-authorized Windows source plus SplitOS-owned release inputs, and for carrying that release through post-install Initial Provisioning to first-use readiness.

The package answers:

```text
What source may be accepted?
How is the source identified and validated?
What exactly is a Build Manifest?
Which typed operations may the Builder execute?
How are Windows components classified and versioned?
How are dependencies and servicing/recovery risks handled?
What first-party package payload must installation media contain?
How does post-install Initial Provisioning work?
How are required vs deferred third-party packages separated?
When is a transformed image considered a verified SplitOS baseline?
When is an installed SplitOS release ready for normal First Run?
What evidence/receipt must be produced?
```

## Core invariant

```text
Microsoft-authorized Windows source
+
SplitOS Release Build Manifest
+
complete required SplitOS first-party package set
+
Initial Provisioning plan/catalog
+
versioned Component Matrix
+
validated servicing mechanisms
↓
Builder transformation / media preparation
↓
post-transform verification
↓
Verified SplitOS Baseline artifact
↓
clean install / Windows OOBE
↓
Initial Provisioning
↓
READY_FOR_FIRST_RUN
or READY_WITH_DEFERRED_OPTIONALS
```

The Builder does **not** create a supported release by applying arbitrary tweaks to an unknown Windows installation.

Initial Provisioning does **not** turn remote metadata into arbitrary package execution.

## Build-time vs provisioning vs runtime

```text
BUILD-TIME
→ composition of supported Windows baseline/media
→ stage exact release payload + provisioning plan

INITIAL PROVISIONING
→ installed-machine preparation after first Windows sign-in
→ required platform + first-party verification
→ approved third-party preparation
→ first-use readiness

RUNTIME
→ account/entitlement
→ normal FREE experience
→ WORK / GAME orchestration where authorized
```

Runtime MUST NOT repeatedly perform destructive build-time removal as ordinary mode-management behavior.

First Run account onboarding MUST NOT begin before mandatory Initial Provisioning readiness on a new clean install.

## Package model

Initial Provisioning distinguishes:

```text
REQUIRED_PLATFORM
FIRST_PARTY_BUNDLED
THIRD_PARTY_PROVISIONED
```

For required SplitOS-owned packages:

```text
release-owned package
→ complete artifact present on installation media/local staged store
→ no SplitOS CDN/backend required for first installation
```

For external software:

```text
provisioned by SplitOS
!= owned by SplitOS
```

Only release-approved/vendor-authorized acquisition mechanisms are allowed. Network/vendor failure may defer an optional item without invalidating core SplitOS readiness.

## Files

```text
Builder and Source Contract.md
Build Manifest Specification.md
Offline Servicing Execution Model.md
Windows Component Matrix.md
Component Dependency and Validation Rules.md
Baseline Verification and Build Receipt.md
Installation Media and Setup Provisioning.md
Initial Provisioning and Package Delivery.md
SPEC-10 Traceability.md
builder-pipeline.mmd
component-decision.mmd
```

## Normative posture

A component classification is not a folklore debloat decision.

```text
candidate
→ exact technical identity
→ dependency analysis
→ supported servicing mechanism
→ build experiment
→ boot / servicing / recovery / compatibility verification
→ ACCEPTED classification
```

Until this chain is complete, destructive candidates remain `TBD` even if the desired product direction is `REMOVE`.

A package installation is also not verified merely because an installer returned success:

```text
installer success
!= installed package/version verified
!= provisioning readiness
```

## Current high-level v1 posture

Examples only; authoritative rows live in `Windows Component Matrix.md`.

```text
Core servicing / boot / recovery        KEEP
Microsoft Store deployment substrate    KEEP
Core networking / display / audio       KEEP
Phone Link / Cross-Device               MODE_MANAGED candidate
Windows Search/indexing                  MODE_MANAGED candidate
Print subsystem                          MODE_MANAGED candidate
Consumer/promotional provisioned apps   REMOVE candidate when removable/validated
OneDrive baseline provisioning           TBD / REMOVE candidate
Microsoft Defender Antivirus             TBD / desired REMOVE candidate, NOT ACCEPTED
Edge browser shell                       TBD / removal requires release-specific validation
WebView2/runtime dependencies             KEEP candidate
Gaming Services/package infrastructure   KEEP / version-specific dependency knowledge
Xbox/Game Bar presentation apps          TBD / capability-specific
```

`TBD / candidate` is intentional. SPEC-10 does not convert product preference into unsupported Windows servicing claims.

## Microsoft servicing basis

The v1 executor is designed around supported Windows deployment/servicing primitives, primarily:

- DISM mounted/offline image servicing;
- package add/remove;
- provisioned AppX inventory/removal where supported;
- optional-feature servicing;
- driver servicing where explicitly release-approved;
- unattended Setup configuration passes such as `offlineServicing`, `specialize`, and `oobeSystem`;
- explicit commit/discard of mounted image changes.

Arbitrary shell/script execution is not a Build Manifest operation type.

## Security boundary

Build Manifest, Initial Provisioning plan and package catalogs are security-sensitive release knowledge. They MAY reference only typed, release-owned operation/package/adapter identifiers.

Forbidden concepts include:

```text
RunPowerShell(<arbitrary>)
RunCommand(<arbitrary>)
DeletePath(<arbitrary>)
WriteRegistry(<arbitrary path>)
DownloadAndRun(<remote url>, <args>)
InstallThirdParty(<url>, <commandLine>, runAsAdmin=true)
```

Exact signing/key hierarchy is owned by SPEC-12, but SPEC-10 requires release manifests/packages/provisioning inputs to be verifiable before execution.

## Build success rule

```text
executor returned zero
!=
baseline verified
```

A build is successful only when all mandatory operations and postconditions are verified against the transformed image/media and the resulting artifact receives a `BuildReceipt` bound to its source identity, manifest, package set, component matrix, provisioning input set and verification results.

Partial output is never silently promoted to a supported SplitOS baseline.

## Installed first-use readiness rule

```text
Windows booted
!=
SplitOS ready for first use
```

A new clean installation reaches normal SplitOS First Run only after:

```text
all required platform predicates verified
+
all required first-party predicates verified
+
provisioning journal/release identity coherent
```

Optional/recommended external software may remain deferred and be completed later through SplitOS Manager.
