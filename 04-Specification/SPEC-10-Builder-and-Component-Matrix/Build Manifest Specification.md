# SplitOS — Build Manifest Specification

## 1. Purpose

`BuildManifest` is the canonical, versioned, machine-readable definition of how one supported Windows base is transformed into one SplitOS release baseline and prepared installation media.

It is product/release knowledge, not a user settings file.

Initial Provisioning is a later installed-machine flow, but the Builder MUST stage and bind the exact release package/catalog/plan inputs that Initial Provisioning is allowed to consume.

---

## 2. Serialization

v1 SHOULD use a strict JSON document validated against a release-owned JSON Schema.

Reasons:

- deterministic typed structure;
- straightforward schema validation;
- no executable semantics;
- easy canonicalization for signing/digesting in SPEC-12;
- language/runtime-neutral tooling.

YAML MAY be used for authoring tooling, but production execution MUST consume the canonical validated representation.

---

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
  "thirdPartyProvisioningCatalogId": "thirdparty-v1",
  "initialProvisioningPlanId": "provisioning-1.0.0",
  "operations": [ ],
  "setupProvisioning": { },
  "verificationProfile": "baseline-v1"
}
```

`thirdPartyProvisioningCatalogId` MAY be null for a release that provisions no external software.

---

## 4. Manifest identity

The manifest MUST expose:

```text
schemaVersion
manifestId
SplitOS release identity
componentMatrixVersion
source constraint identity
initialProvisioningPlanId
```

A manifest for one Windows base MUST NOT silently execute against another base merely because field names still parse.

The referenced Initial Provisioning plan MUST be release-bound and MUST NOT be substituted with a plan from another release solely because its schema is compatible.

---

## 5. Operation model

Every operation MUST be typed and refer to release-owned identifiers.

Minimum operation envelope:

```text
operationId
operationType
phase
required | optional
dependsOn[]
componentId / packageId / policyId / provisioningPlanId where applicable
preconditions[]
expectedPostconditions[]
failurePolicy
```

`required=false` does not mean failure may be hidden. It means the release definition explicitly permits the operation to be absent/skipped while still producing a supported baseline.

---

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
STAGE_PROVISIONING_CATALOG
REGISTER_INITIAL_PROVISIONING_PLAN
REGISTER_SETUP_PROVISIONING
ADD_APPROVED_DRIVER_PACKAGE
STAGE_RECOVERY_ASSET
ASSERT_COMPONENT_STATE
```

Not every operation type must be enabled for every release.

`REGISTER_SETUP_PROVISIONING` registers the Windows Setup/bootstrap entry points. `REGISTER_INITIAL_PROVISIONING_PLAN` binds the post-sign-in provisioning transaction to a verified release-owned plan. They are related but not interchangeable.

---

## 7. Forbidden generic operations

The Build Manifest MUST NOT contain general execution primitives:

```text
RUN_COMMAND
RUN_POWERSHELL
RUN_SCRIPT
DELETE_ARBITRARY_PATH
WRITE_ARBITRARY_REGISTRY
COPY_ARBITRARY_BINARY_AND_EXECUTE
DOWNLOAD_AND_RUN
REMOVE_ARBITRARY_PACKAGE_NAME_FROM_UI
```

The Build Manifest and Initial Provisioning plan MUST NOT accept an unbounded remote/software descriptor such as:

```text
url
commandLine
runAsAdmin
```

as sufficient authority to execute software.

When a new servicing or provisioning behavior is needed, it becomes a new typed operation/handler with explicit validation and implementation ownership.

---

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

---

## 9. Phase model

Recommended v1 phases:

```text
P00_SOURCE_ASSERT
P10_OFFLINE_PACKAGE_REMOVAL
P20_OFFLINE_FEATURE_BASELINE
P30_OFFLINE_POLICY_AND_SERVICE_BASELINE
P40_SPLITOS_PAYLOAD_STAGING
P45_PROVISIONING_CATALOG_STAGING
P50_SETUP_PROVISIONING
P60_RECOVERY_ASSETS
P70_OFFLINE_VERIFICATION
P80_COMMIT_IMAGE
P90_MEDIA_ASSEMBLY
P100_OUTPUT_VERIFICATION
```

Dependencies, not incidental JSON list order, determine operation ordering inside a phase.

`P45` stages data/artifacts required by the later installed-machine Initial Provisioning flow; it does not execute that flow inside the mounted image.

---

## 10. Preconditions

An operation MUST be able to state bounded preconditions such as:

```text
COMPONENT_PRESENT
PACKAGE_IDENTITY_MATCHES
FEATURE_STATE_IN {ENABLED, DISABLED}
SOURCE_BUILD_MATCHES
DEPENDENCY_CLASS_ACCEPTED
NO_FORBIDDEN_CONSUMER_PRESENT
PROVISIONING_PLAN_RELEASE_MATCHES
FIRST_PARTY_ARTIFACT_PRESENT
```

A failed required precondition produces a typed build failure; the executor must not improvise a workaround.

---

## 11. Postconditions

Every mutating required operation MUST declare verifiable postconditions.

Examples:

```text
PROVISIONED_APPX_ABSENT
OPTIONAL_FEATURE_DISABLED
PACKAGE_ABSENT
MANAGED_SERVICE_BASELINE_MATCHES
SPLITOS_PACKAGE_STAGED_WITH_DIGEST
PROVISIONING_PLAN_STAGED_WITH_DIGEST
REQUIRED_FIRST_PARTY_PAYLOAD_COMPLETE
POLICY_VALUE_MATCHES_RELEASE_DEFINITION
```

Command exit code alone is not a postcondition.

A media build that omits a `requiredForFirstUse` first-party artifact cannot be promoted to a supported release output merely because the offline Windows image itself boots.

---

## 12. Failure policy

Allowed high-level policies:

```text
FAIL_BUILD
SKIP_IF_ABSENT
WARN_AND_CONTINUE
```

`WARN_AND_CONTINUE` is valid only for release-defined optional behavior and MUST appear in the BuildReceipt.

Required baseline and required first-use payload invariants MUST use `FAIL_BUILD`.

---

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

Initial Provisioning has its own durable resume/reconciliation contract in `Initial Provisioning and Package Delivery.md`.

---

## 14. PackageSet

`packageSet` describes SplitOS-owned release packages, not arbitrary external applications.

Each entry MUST include at least:

```text
packageId
version
packageClass
artifactName/type
digest
sourceKind
requiredForFirstUse
installation/provisioningPhase
capabilityIds[]
compatibilityConstraints
installHandlerId
verifyHandlerId
publisherConstraint where applicable
```

Allowed first-party semantic classes:

```text
REQUIRED_PLATFORM
FIRST_PARTY_BUNDLED
```

For release-owned payload used in Initial Provisioning:

```text
sourceKind = LOCAL_RELEASE_BUNDLE
```

A package may be present and installed for all users while runtime entitlement decides whether a premium capability may become active.

```text
installed package
!= active capability authorization
```

Signing/key validation is completed by SPEC-12; SPEC-10 requires artifact integrity/provenance to be verifiable before staging and installation.

---

## 15. Third-party provisioning catalog

Third-party software is not automatically promoted into the SplitOS-owned `packageSet`.

A release MAY reference a separate versioned third-party provisioning catalog.

Minimum conceptual item:

```text
softwareId
adapterId
recommended/optional policy
acquisitionMode
publisherConstraint
version/freshness policy where applicable
verificationPolicyId
userActionPolicy
```

Allowed acquisition mode families include only validated bounded mechanisms such as:

```text
MEDIA_BUNDLED_VENDOR_AUTHORIZED
OFFICIAL_VENDOR_ONLINE
SUPPORTED_STORE_ACQUISITION
```

The external vendor/platform retains ownership of its application, account, license and update semantics.

The Builder MUST verify that any third-party binary physically included on prepared media is authorized for redistribution under the release policy.

---

## 16. SetupProvisioning

The manifest MAY reference only typed, release-owned setup provisioning templates/actions.

Conceptual data:

```text
unattendTemplateId
specializeBootstrapId
firstBootValidationId
initialProvisioningPlanId
packageCatalogId
thirdPartyProvisioningCatalogId
OOBE policy identifiers
```

The setup bootstrap establishes the machine substrate and a trusted path into FL-00 Initial Provisioning after the first supported Windows sign-in.

Raw arbitrary user-supplied unattend/script content is outside the supported manifest contract.

---

## 17. Initial Provisioning plan relation

The Build Manifest defines/stages the inputs. The Initial Provisioning plan defines the installed-machine ordered preparation.

```text
Build Manifest
→ stage exact release payload/catalog/plan

Initial Provisioning
→ consume those staged verified inputs
→ install/verify REQUIRED_PLATFORM
→ install/verify FIRST_PARTY_BUNDLED
→ attempt THIRD_PARTY_PROVISIONED
→ publish readiness
```

The Builder MUST NOT mark the future installed system `READY_FOR_FIRST_RUN`; it can only verify that the media contains a complete, internally consistent provisioning input set.

---

## 18. Manifest evolution

Unknown `schemaVersion` is a hard failure.

Unknown required operation type is a hard failure.

Unknown required package class/handler is a hard failure.

Unknown optional metadata field MAY be ignored only when schema compatibility explicitly permits it.

---

## 19. Manifest security handoff

SPEC-10 defines the semantic object.

SPEC-12 defines:

```text
canonical serialization
signature envelope
release signing keys
rotation/revocation
manifest/artifact trust verification
```

Until signature verification succeeds, production Builder MUST NOT execute the manifest or trust its provisioning-plan/package bindings.
