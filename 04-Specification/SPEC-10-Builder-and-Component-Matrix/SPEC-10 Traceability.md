# SPEC-10 — Traceability

## 1. Source chain

```text
Discovery / Decisions
→ Distribution & Entitlement Requirements
→ A&D Build Pipeline / Component Classification / Ownership / Trust
→ Synthesis
→ SPEC-10 Builder & Component Matrix
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
```

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

### Build/runtime separation

```text
build-time composition
!=
runtime mode control
```

Preserved by:

- Offline Servicing Execution Model;
- Installation Media and Setup Provisioning;
- Component Matrix MODE_MANAGED semantics.

### Disclosure / entitlement

```text
FR-SETUP-008..010
→ Installation Media and Setup Provisioning
```

## 4. A&D mapping

### Boundaries

```text
03-Analysis-and-Design/00-Boundaries/SplitOS Build Pipeline.md
→ source / validation / offline servicing / media / clean install
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
→ package set
→ supported source decision
→ verified baseline definition
```

Microsoft remains authority for Windows binaries/licensing/upstream implementation.

### Data

```text
WindowsBase
BuildManifest
WindowsComponentDefinition
ComponentClassificationDecision
SplitOSRelease
InstalledBaselineIdentity
```

map to SPEC-10 source, manifest, matrix, receipt and provisioning artifacts.

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

### Failures

Build failure principle:

```text
mandatory action failed
→ baseline not verified
→ unsupported output
```

is implemented by operation/postcondition verification and BuildReceipt failure states.

### Trust

A&D Trust establishes:

```text
Build Manifest is security-sensitive
arbitrary shell actions forbidden
release artifacts require trust verification
```

SPEC-10 defines the typed semantic contract; SPEC-12 defines signatures/key hierarchy.

## 5. Downstream handoff

### SPEC-11 Update & Recovery

Consumes:

```text
InstalledBaselineIdentity
BuildReceipt/baseline descriptors
release package identities
recovery assets
component matrix knowledge
```

### SPEC-12 Release Security

Must define trust for:

```text
Build Manifest
Component Matrix
source catalog
SplitOS package set
baseline descriptor / receipt where applicable
```

### SPEC-14 Verification

Must convert the Component Validation Ladder into executable acceptance suites:

```text
boot
OOBE
first sign-in
Windows servicing
recovery
runtime
major applications/integrations
```

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
SPEC-DEC-127  SplitOS Account sign-in remains post-Windows-user first-run behavior, not a Windows Setup authentication dependency
SPEC-DEC-128  clean installation of a verified prepared baseline is the supported v1 path; arbitrary existing-Windows mutation is not equivalent
```
