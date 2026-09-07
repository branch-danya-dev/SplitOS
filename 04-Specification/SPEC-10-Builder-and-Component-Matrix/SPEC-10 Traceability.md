# SPEC-10 — Traceability

## 1. Source chain

```text
Discovery / Decisions
→ Distribution / Initial Provisioning Requirements
→ A&D Build Pipeline / FL-00 Initial Provisioning / Ownership / Trust
→ Synthesis
→ SPEC-10 Builder / Component Matrix / Initial Provisioning
```

## 2. Primary source decisions

SPEC-10 preserves the decisions established around:

```text
DEC-028 Windows source as Microsoft-owned external input
DEC-029 versioned Build Manifest / prepared baseline
DEC-030 REMOVE / DISABLE / MODE_MANAGED / KEEP classification
DEC-031 MODE_MANAGED lifecycle
DEC-032 build-time vs runtime separation
DEC-033 SplitOS account/entitlement separation
DEC-034 paid capability disclosure before destructive install
DEC-048 complete required first-party release payload on installation media
DEC-049 separate Initial Provisioning before normal First Run
DEC-050 REQUIRED_PLATFORM / FIRST_PARTY_BUNDLED / THIRD_PARTY_PROVISIONED
DEC-051 vendor-authorized third-party acquisition + deferred/retryable semantics
```

---

## 3. Requirements mapping

### FR-BUILD source validation

```text
FR-BUILD-001..006
→ Builder and Source Contract
→ Build Manifest Specification
```

Covers:

```text
supported Windows base
external Microsoft-owned source
no assumed public modified-ISO redistribution
source compatibility validation
unsupported source rejection
source-acquisition mechanism remains legally/technically validated
```

### FR-BUILD manifest/reproducibility

```text
versioned Build Manifest
→ Build Manifest Specification
→ Baseline Verification and Build Receipt
```

### Component lifecycle requirements

```text
REMOVE / DISABLE / MODE_MANAGED / KEEP
→ Windows Component Matrix
→ Component Dependency and Validation Rules
→ Offline Servicing Execution Model
```

### Initial Provisioning requirements

```text
FR-PROV-001..062
→ Installation Media and Setup Provisioning
→ Initial Provisioning and Package Delivery
→ Build Manifest package/provisioning-plan binding
```

Preserves:

```text
Windows deployed != SplitOS ready for first use
complete required first-party payload available locally
account/backend not required for bundled first-party installation
required package failure blocks readiness
optional third-party failure may defer
vendor-authorized bounded acquisition only
First Run begins only after provisioning readiness
```

### Build / provisioning / runtime separation

```text
build-time composition
!=
Initial Provisioning
!=
runtime mode/account behavior
```

Preserved by:

- Offline Servicing Execution Model;
- Installation Media and Setup Provisioning;
- Initial Provisioning and Package Delivery;
- Component Matrix MODE_MANAGED semantics.

### Disclosure / entitlement

```text
FR-SETUP-008..010
→ Installation Media and Setup Provisioning
```

Entitlement remains orthogonal to package installation:

```text
first-party package installed
!= premium capability authorized
```

---

## 4. A&D mapping

### Boundaries

```text
03-Analysis-and-Design/00-Boundaries/SplitOS Build Pipeline.md
→ source / validation / offline servicing / media / clean install
```

### Flows

```text
03-Analysis-and-Design/08-Flows/Initial Provisioning Flow.md
→ FL-00
→ required platform
→ first-party bundled features
→ approved third-party preparation
→ readiness
→ FL-01 First Run
```

### Component classification

```text
Windows Component Classification Model.md
→ Windows Component Matrix.md
```

The A&D classification becomes an executable release-governance model only after mechanism/dependency/validation evidence.

### Responsibilities / Ownership

```text
Distribution Engineering
→ Build Manifest
→ Component Matrix
→ first-party package set
→ provisioning plan/catalog
→ supported source decision
→ verified baseline/media definition

Initial Provisioning Coordination
→ installed-machine provisioning transaction
→ package verification
→ readiness result
```

Microsoft remains authority for Windows binaries/licensing/upstream implementation.

External vendors remain authority for their provisioned third-party software, licenses/accounts/update semantics.

### Data

```text
WindowsBase
BuildManifest
WindowsComponentDefinition
ComponentClassificationDecision
SplitOSRelease
InstalledBaselineIdentity
ProvisioningPlan
ProvisioningTransaction
ProvisioningItemResult
```

map to SPEC-10 source, manifest, matrix, receipt, package catalog and provisioning artifacts.

### Interfaces / Integrations

A&D Build integration:

```text
Windows source
→ validation
→ typed manifest executor
→ DISM/offline servicing
→ validation
```

becomes concrete in `Offline Servicing Execution Model.md`.

A&D provisioning integration:

```text
local release package store / approved vendor mechanism
→ typed package handler/adapter
→ install/acquire
→ actual-state read-back
→ readiness verification
```

becomes concrete in `Initial Provisioning and Package Delivery.md`.

### Failures

Build failure principle:

```text
mandatory build action failed
→ baseline not verified
→ unsupported output
```

Provisioning failure principle:

```text
required platform/first-party verification failed
→ First Run readiness prohibited

optional/recommended third-party unavailable
→ deferred/retryable
→ core readiness may continue
```

### Trust

A&D Trust establishes:

```text
Build Manifest is security-sensitive
arbitrary shell actions forbidden
release artifacts require trust verification
external metadata cannot become direct privileged command input
```

SPEC-10 applies the same rule to provisioning plans/catalogs; SPEC-12 defines signatures/key hierarchy.

---

## 5. Downstream handoff

### FL-01 / SPEC-04 Account

Consumes only a verified provisioning readiness result on a new clean installation:

```text
READY_FOR_FIRST_RUN
or READY_WITH_DEFERRED_OPTIONALS
```

Account auth does not install the release-owned platform package set.

### SPEC-11 Update & Recovery

Consumes:

```text
InstalledBaselineIdentity
BuildReceipt/baseline descriptors
release package identities
recovery assets
component matrix knowledge
```

Initial Provisioning installs the media release; a newer release later uses the normal SPEC-11 update transaction.

### SPEC-12 Release Security

Must define trust for:

```text
Build Manifest
Component Matrix
source catalog
SplitOS package set
ProvisioningPlan / package catalogs
third-party provisioning catalog where release-authored
baseline descriptor / receipt where applicable
```

### SPEC-14 Verification

Must convert the validation model into executable acceptance suites:

```text
boot
OOBE
first sign-in
offline first-party provisioning
required artifact corruption/failure
third-party network/vendor defer
provisioning crash/reboot/resume
First Run readiness gating
Windows servicing
recovery
runtime
major applications/integrations
```

---

## 6. SPEC decisions introduced

```text
SPEC-DEC-109  v1 production source path includes USER_PROVIDED_SOURCE; automatic acquisition remains OPEN
SPEC-DEC-110  source is immutable and transformed only through a working copy
SPEC-DEC-111  BuildManifest canonical execution representation is strict schema-validated JSON
SPEC-DEC-112  manifest operations are typed; arbitrary shell/PowerShell/registry/path operations are forbidden
SPEC-DEC-113  exact component technical identity is release/Windows-base scoped and resolved from Component Matrix
SPEC-DEC-114  build executor uses supported Windows offline servicing/configuration mechanisms with postcondition read-back
SPEC-DEC-115  failed required precondition/execution/postcondition fails the build; no silent supported partial baseline
SPEC-DEC-116  v1 normal crash recovery discards uncommitted workspace and rebuilds from immutable source
SPEC-DEC-117  RELEASE_VALIDATED and BUILD_INSTANCE_VERIFIED are distinct states
SPEC-DEC-118  successful build emits BuildReceipt bound to source/manifest/matrix/package/toolchain/evidence
SPEC-DEC-119  semantic baseline identity does not require byte-identical WIM/ISO packaging output
SPEC-DEC-120  REMOVE requires mechanism+boot+servicing+recovery+compatibility evidence before production acceptance
SPEC-DEC-121  group/capability matrix rows are not direct servicing targets; exact child mappings are required
SPEC-DEC-122  Microsoft Store/application deployment substrate is KEEP in current baseline; Store removal is not a supported target
SPEC-DEC-123  Defender Antivirus remains desired REMOVE candidate but TBD/not production-accepted until minimum security and servicing/dependency validation
SPEC-DEC-124  Edge browser shell and WebView2/runtime are separate component decisions
SPEC-DEC-125  Microsoft Gaming Services/package dependencies cannot be blanket-removed while Microsoft Gaming support is in scope
SPEC-DEC-126  SplitOS package payload is staged offline and installed/registered through supported Setup/bootstrap provisioning rather than treated as an arbitrary app installed into a mounted image
SPEC-DEC-127  SplitOS Account sign-in remains post-Windows-user behavior and now follows mandatory Initial Provisioning readiness
SPEC-DEC-128  clean installation of a verified prepared baseline is the supported v1 path; arbitrary existing-Windows mutation is not equivalent
SPEC-DEC-129  installation media contains every requiredForFirstUse SplitOS-owned platform/first-party artifact for the release
SPEC-DEC-130  Initial Provisioning is a durable installed-machine flow distinct from Builder execution and normal First Run
SPEC-DEC-131  provisioning package classes are REQUIRED_PLATFORM / FIRST_PARTY_BUNDLED / THIRD_PARTY_PROVISIONED
SPEC-DEC-132  optional third-party provisioning may defer without invalidating verified core readiness
SPEC-DEC-133  third-party provisioning uses bounded release-owned adapters and vendor-authorized acquisition; generic remote download-and-run is forbidden
SPEC-DEC-134  installer/process success is not provisioning success; installed-state read-back and mandatory readiness verification are required
```
