# SplitOS — Baseline Verification and Build Receipt

## 1. Purpose

This document defines when a Builder output may be called a verified SplitOS baseline artifact and what durable evidence must be emitted.

## 2. Distinguish two validations

```text
RELEASE_VALIDATED
!=
BUILD_INSTANCE_VERIFIED
```

### RELEASE_VALIDATED

Means release engineering has validated the source/matrix/manifest/package combination through the required boot, servicing, recovery and compatibility suite.

### BUILD_INSTANCE_VERIFIED

Means one concrete user/developer Builder execution transformed an approved source and verified all release-defined postconditions.

The Builder cannot replace release validation with local static checks.

## 3. Build states

```text
CREATED
SOURCE_VALIDATING
SOURCE_REJECTED
PREPARING_WORKSPACE
SERVICING
VERIFYING_OFFLINE
COMMITTING_IMAGE
ASSEMBLING_MEDIA
VERIFYING_OUTPUT
SUCCEEDED
FAILED
CANCELLED
```

`SUCCEEDED` is terminal only after output verification.

## 4. Offline verification profile

Before image commit the Builder MUST verify all mandatory manifest postconditions.

Minimum families:

```text
source identity still bound to approved release input
required REMOVE targets absent/deprovisioned as defined
required DISABLE targets at expected baseline state
MODE_MANAGED targets still present
KEEP invariants present
SplitOS package payloads staged with expected digest
setup/provisioning assets present
recovery assets present
no required operation unresolved
```

## 5. Commit semantics

Image servicing commit means:

> changes were written to the working image.

It does not yet mean:

> install media is valid and supported.

After commit, media/deployment output still requires verification.

## 6. Output verification

Minimum output checks:

```text
required boot/setup files present
install image readable
selected image/index present
transformed image metadata readable
SplitOS build identity metadata present
expected media layout present
artifact hashes calculable
no temporary/mount paths accidentally embedded as product dependencies
```

Where practical the Builder SHOULD perform DISM/image integrity checks on output artifacts.

## 7. BuildReceipt

Every successful build emits immutable `BuildReceipt` data.

Minimum conceptual schema:

```text
receiptVersion
buildId
builderVersion
buildStartedAt
buildCompletedAt

SourceIdentity
selected image/index
BuildManifest id/version/digest
Component Matrix version/digest
SplitOS release
SplitOS package ids/versions/digests
servicing toolchain/ADK/DISM identity

operationResults[]
verificationResults[]
warningResults[]
output artifact identity/digests
finalStatus
```

## 8. OperationResult

For each manifest operation:

```text
operationId
componentId/packageId
operationType
required
result
mechanism
precondition evidence ref
postcondition evidence ref
startedAt
completedAt
error category/details when failed
```

Allowed normalized outcomes include:

```text
VERIFIED_APPLIED
VERIFIED_NOOP
SKIPPED_OPTIONAL
FAILED_PRECONDITION
FAILED_EXECUTION
FAILED_POSTCONDITION
UNSUPPORTED_MECHANISM
```

## 9. Warnings

A supported build may contain only release-authorized warnings.

Example:

```text
optional cosmetic app absent in source already
→ VERIFIED_NOOP / SKIPPED_OPTIONAL
```

Not acceptable:

```text
Defender removal failed
→ warning
→ build success
```

when Defender removal is a required accepted baseline action.

## 10. Build failure result

A failure receipt SHOULD still be emitted where possible.

It contains:

```text
buildId
source/manifest identity
last successful phase
failed operation
failure category
servicing log references
workspace disposition = DISCARDED / RETAINED_FOR_DIAGNOSTIC
```

A failed artifact MUST NOT receive a supported baseline identity.

## 11. Baseline identity generation

After successful verification, Builder derives an installation-facing baseline descriptor from:

```text
SplitOS release
source catalog identity
manifest identity/digest
matrix identity/digest
package set identity/digests
verification profile
```

This descriptor is staged so installed runtime can establish `InstalledBaselineIdentity` during provisioning.

## 12. Semantic reproducibility

Two successful builds from the same approved inputs SHOULD produce the same semantic baseline identity even if ISO/WIM byte hashes differ due to packaging/compression metadata.

Artifact hashes are still recorded for integrity/diagnostics.

## 13. No self-certification of unknown source

The Builder MUST NOT generate `SUCCEEDED/SUPPORTED` merely because all requested operations happened to apply to a source outside the release source policy.

```text
unknown source
+
all commands succeeded
!=
supported SplitOS baseline
```

## 14. Installation handoff

The installer/runtime receives:

```text
baseline descriptor
release identity
required runtime package identity
recovery/update compatibility metadata
```

It does not need the entire verbose Builder log to boot, but the full BuildReceipt remains useful for support/reproducibility.

## 15. Tamper resistance handoff

SPEC-12 defines:

```text
receipt signing if required
manifest/artifact signature verification
trusted release metadata
```

SPEC-10 only defines what semantic evidence must exist.
