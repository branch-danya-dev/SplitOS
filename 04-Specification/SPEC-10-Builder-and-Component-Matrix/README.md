# SPEC-10 — Builder & Windows Component Matrix

## Status

`READY FOR REVIEW`

## Purpose

SPEC-10 turns the build-time architecture into an implementable contract for producing a supported SplitOS Windows baseline from a validated Microsoft-authorized Windows source plus SplitOS-owned release inputs.

The package answers:

```text
What source may be accepted?
How is the source identified and validated?
What exactly is a Build Manifest?
Which typed operations may the Builder execute?
How are Windows components classified and versioned?
How are dependencies and servicing/recovery risks handled?
When is a transformed image considered a verified SplitOS baseline?
What evidence/receipt must be produced?
```

## Core invariant

```text
Microsoft-authorized Windows source
+
SplitOS Release Build Manifest
+
SplitOS package set
+
versioned Component Matrix
+
validated servicing mechanisms
↓
Builder transformation
↓
post-transform verification
↓
Verified SplitOS Baseline artifact
```

The Builder does **not** create a supported release by applying arbitrary tweaks to an unknown Windows installation.

## Build-time vs runtime

```text
BUILD-TIME
→ composition of the supported Windows baseline

RUNTIME
→ operation of WORK / GAME on the installed baseline
```

Runtime MUST NOT repeatedly perform destructive build-time removal as ordinary mode-management behavior.

## Files

```text
Builder and Source Contract.md
Build Manifest Specification.md
Offline Servicing Execution Model.md
Windows Component Matrix.md
Component Dependency and Validation Rules.md
Baseline Verification and Build Receipt.md
Installation Media and Setup Provisioning.md
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

Build Manifest is security-sensitive release knowledge. It MAY reference only typed, release-owned operation and component identifiers.

Forbidden manifest concepts include:

```text
RunPowerShell(<arbitrary>)
RunCommand(<arbitrary>)
DeletePath(<arbitrary>)
WriteRegistry(<arbitrary path>)
RemovePackage(<UI supplied package name>)
```

Exact signing/key hierarchy is owned by SPEC-12, but SPEC-10 requires release manifests/packages to be verifiable before execution.

## Build success rule

```text
executor returned zero
!=
baseline verified
```

A build is successful only when all mandatory operations and postconditions are verified against the transformed image and the resulting artifact receives a `BuildReceipt` bound to its source identity, manifest, package set, component matrix and verification results.

Partial output is never silently promoted to a supported SplitOS baseline.
