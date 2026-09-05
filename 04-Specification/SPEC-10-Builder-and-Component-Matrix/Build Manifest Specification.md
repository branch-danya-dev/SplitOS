# SplitOS — Build Manifest Specification

## 1. Purpose

`BuildManifest` is the canonical, versioned, machine-readable definition of how one supported Windows base is transformed into one SplitOS release baseline.

It is product/release knowledge, not a user settings file.

## 2. Serialization

v1 SHOULD use a strict JSON document validated against a release-owned JSON Schema.

Reasons:

- deterministic typed structure;
- straightforward schema validation;
- no executable semantics;
- easy canonicalization for signing/digesting in SPEC-12;
- language/runtime-neutral tooling.

YAML MAY be used for authoring tooling, but production execution MUST consume the canonical validated representation.

## 3. Top-level shape

Conceptual example:

```json
{
  "schemaVersion": 1,
  "manifestId": "splitos-1.0-win11-x64-baseA",
  "splitOsRelease": "1.0.0",
  "sourceConstraint": { },
  "componentMatrixVersion": "1.0.0",
  "toolchainConstraint": { },
  "packageSet": [ ],
  "operations": [ ],
  "setupProvisioning": { },
  "verificationProfile": "baseline-v1"
}
```

## 4. Manifest identity

The manifest MUST expose:

```text
schemaVersion
manifestId
SplitOS release identity
componentMatrixVersion
source constraint identity
```

A manifest for one Windows base MUST NOT silently execute against another base merely because field names still parse.

## 5. Operation model

Every operation MUST be typed and refer to release-owned identifiers.

Minimum operation envelope:

```text
operationId
operationType
phase
required | optional
dependsOn[]
componentId / packageId / policyId where applicable
preconditions[]
expectedPostconditions[]
failurePolicy
```

`required=false` does not mean failure may be hidden. It means the release definition explicitly permits the operation to be absent/skipped while still producing a supported baseline.

## 6. Supported v1 operation types

The executor MAY implement only explicitly versioned operations such as:

```text
INVENTORY_ASSERT
REMOVE_PROVISIONED_APPX
DISABLE_OPTIONAL_FEATURE
REMOVE_OS_PACKAGE
SET_MANAGED_SERVICE_BASELINE
APPLY_BASELINE_POLICY
STAGE_SPLITOS_PACKAGE
REGISTER_SETUP_PROVISIONING
ADD_APPROVED_DRIVER_PACKAGE
STAGE_RECOVERY_ASSET
ASSERT_COMPONENT_STATE
```

Not every operation type must be enabled for every release.

## 7. Forbidden generic operations

The Build Manifest MUST NOT contain general execution primitives:

```text
RUN_COMMAND
RUN_POWERSHELL
RUN_SCRIPT
DELETE_ARBITRARY_PATH
WRITE_ARBITRARY_REGISTRY
COPY_ARBITRARY_BINARY_AND_EXECUTE
REMOVE_ARBITRARY_PACKAGE_NAME_FROM_UI
```

When a new servicing behavior is needed, it becomes a new typed operation with explicit validation and implementation ownership.

## 8. Component references

Component mutation operations MUST refer to a stable SplitOS-owned `componentId` from the Component Matrix.

Example:

```json
{
  "operationType": "REMOVE_PROVISIONED_APPX",
  "componentId": "WIN.APPX.FEEDBACK_HUB",
  "required": true
}
```

The executor resolves the exact package identity for the current supported Windows base through release knowledge.

The manifest MUST NOT accept a raw package full-name supplied interactively by a user.

## 9. Phase model

Recommended v1 phases:

```text
P00_SOURCE_ASSERT
P10_OFFLINE_PACKAGE_REMOVAL
P20_OFFLINE_FEATURE_BASELINE
P30_OFFLINE_POLICY_AND_SERVICE_BASELINE
P40_SPLITOS_PAYLOAD_STAGING
P50_SETUP_PROVISIONING
P60_RECOVERY_ASSETS
P70_OFFLINE_VERIFICATION
P80_COMMIT_IMAGE
P90_MEDIA_ASSEMBLY
P100_OUTPUT_VERIFICATION
```

Dependencies, not incidental JSON list order, determine operation ordering inside a phase.

## 10. Preconditions

An operation MUST be able to state bounded preconditions such as:

```text
COMPONENT_PRESENT
PACKAGE_IDENTITY_MATCHES
FEATURE_STATE_IN {ENABLED, DISABLED}
SOURCE_BUILD_MATCHES
DEPENDENCY_CLASS_ACCEPTED
NO_FORBIDDEN_CONSUMER_PRESENT
```

A failed required precondition produces a typed build failure; the executor must not improvise a workaround.

## 11. Postconditions

Every mutating required operation MUST declare verifiable postconditions.

Examples:

```text
PROVISIONED_APPX_ABSENT
OPTIONAL_FEATURE_DISABLED
PACKAGE_ABSENT
MANAGED_SERVICE_BASELINE_MATCHES
SPLITOS_PACKAGE_STAGED_WITH_DIGEST
POLICY_VALUE_MATCHES_RELEASE_DEFINITION
```

Command exit code alone is not a postcondition.

## 12. Failure policy

Allowed high-level policies:

```text
FAIL_BUILD
SKIP_IF_ABSENT
WARN_AND_CONTINUE
```

`WARN_AND_CONTINUE` is valid only for release-defined optional behavior and MUST appear in the BuildReceipt.

Required baseline invariants MUST use `FAIL_BUILD`.

## 13. Idempotency and restart

The Builder SHOULD execute from a fresh working copy for every normal build.

Operations SHOULD still be state-aware:

```text
already at expected state
→ VERIFIED_NOOP
```

rather than forcing a second destructive action.

After executor crash, v1 SHOULD discard the uncommitted mount/work area and restart from the immutable source unless a future journaled-resume mechanism is explicitly specified.

This keeps build recovery simpler than runtime transaction recovery.

## 14. PackageSet

Each SplitOS-owned package entry MUST include at least:

```text
packageId
version
artifactName/type
digest
installation/provisioning phase
required
compatibility constraints
```

Signing/key validation is completed by SPEC-12; SPEC-10 requires that artifact integrity be verified before staging.

## 15. SetupProvisioning

The manifest MAY reference only typed, release-owned setup provisioning templates/actions.

Example conceptual data:

```text
unattendTemplateId
specializeBootstrapId
firstBootValidationId
OOBE policy identifiers
```

Raw arbitrary user-supplied unattend/script content is outside the supported manifest contract.

## 16. Manifest evolution

Unknown `schemaVersion` is a hard failure.

Unknown required operation type is a hard failure.

Unknown optional metadata field MAY be ignored only when schema compatibility explicitly permits it.

## 17. Manifest security handoff

SPEC-10 defines the semantic object.

SPEC-12 defines:

```text
canonical serialization
signature envelope
release signing keys
rotation/revocation
manifest/artifact trust verification
```

Until signature verification succeeds, production Builder MUST NOT execute the manifest.
