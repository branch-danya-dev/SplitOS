# SPEC-02 — Broker Capability Catalog

## 1. Purpose

This document defines the v1 **allowlisted privileged capability surface**.

The catalog is intentionally semantic and bounded.

Broker does not accept arbitrary commands, raw service names, raw Registry paths or arbitrary executable paths from Runtime Host.

---

## 2. Capability classes

```text
READ_ONLY
MUTATION
MAINTENANCE
RECOVERY
```

Every capability specifies:

```text
capability ID
caller role
session/control-owner requirement
payload schema
allowed target set
idempotency requirement
cancellation class
audit class
technical verification output
```

---

## 3. Common caller rules

Unless explicitly stated otherwise:

```text
callerRole = RuntimeHost
validatedSignedInstalledProcess = true
sameEligibleConsoleSession = true
handshakeCompatible = true
```

No Manager/GameLauncher caller is valid.

---

## 4. Read-only capabilities

### `Broker.Health.Read`

Purpose: verify Broker process/protocol health.

Input:

```text
none
```

Output:

```text
serviceVersion
protocolVersion
readiness
currentSessionEligibility
```

Control-owner required: no, but caller still must be valid Runtime Host.  
Idempotency key: no.  
Cancellation: not applicable.

---

### `Broker.Capabilities.Read`

Purpose: obtain advertised capability set for this version/session.

Output:

```text
capability IDs
capability versions
availability / reason
```

This result does not grant product entitlement by itself.

---

### `Machine.ManagedState.Read`

Purpose: read machine-level state that requires Broker authority/access and is not already available safely through normal user-session Windows APIs.

Input uses typed `stateKind` enum only.

Prohibited:

```text
arbitrary registry path
arbitrary file path
arbitrary service name
```

Output is technical evidence, not canonical mode state.

---

## 5. Mode/runtime machine mutation capabilities

### `Machine.ServicePolicy.Apply`

Purpose: apply a validated set of service lifecycle changes required by a semantic Work/Game policy.

Input:

```text
policyOperationId
entries[] {
    managedServiceId
    desiredState: RUNNING | STOPPED | START_TYPE_POLICY_VALUE
}
```

`managedServiceId` MUST resolve through Broker's release-owned allowlist/catalog.

The caller MUST NOT provide a raw SCM service name unless the schema maps a bounded ID to one exact release-defined service target.

Idempotency: required.  
Cancellation: `CANCELABLE_UNTIL_APPLY` per entry/batch.  
Verification output SHOULD include actual resulting service state for each target.

If one mandatory target cannot be verified, response MUST expose partial/failed result; it cannot return overall success because some calls succeeded.

---

### `Machine.Policy.Apply`

Purpose: apply machine-scoped SplitOS-managed policy values.

Input:

```text
policyOperationId
policyEntries[] {
    managedPolicyId
    desiredValue
}
```

`managedPolicyId` maps to a release-defined allowed Registry/policy/Windows mechanism.

Raw Registry key/value paths are prohibited from IPC payload.

Idempotency: required.  
Cancellation: capability-specific until first non-reversible apply.  
Verification: read back through the same bounded policy mapping.

---

### `Machine.ManagedComponent.Action`

Purpose: perform a predeclared runtime action on a component classified as `MODE_MANAGED` or another explicitly runtime-manageable SplitOS component.

Input:

```text
componentId
actionId
contextId
```

Both identifiers MUST exist in signed/release-owned local configuration.

This is not a generic executable/plugin loader.

---

## 6. Maintenance capabilities

### `Maintenance.Artifact.VerifyStaged`

Purpose: Broker-side verification of a staged SplitOS update/recovery artifact before privileged apply.

Input:

```text
stagedArtifactId
expectedReleaseId
expectedDigestReference
transactionId
```

Raw caller-chosen network URL is prohibited.

Output:

```text
verified | rejected
resolved artifact identity
verification evidence reference
```

Signature/digest/key policy details belong to `SPEC-12`.

---

### `Maintenance.Update.ApplyVerified`

Purpose: begin privileged application of an already authorized/verified update transaction.

Input:

```text
updateTransactionId
verifiedArtifactId
targetReleaseId
```

Prerequisites:

- verified artifact known locally;
- transaction exists in owner-approved state;
- target release transition permitted;
- no incompatible major mutation is executing.

Idempotency: required and bound to update transaction ID.  
Cancellation: capability-defined; likely `CANCELABLE_UNTIL_APPLY`.

Response may be `ACCEPTED` with `brokerJobId`.

Broker completion is not equivalent to baseline commit; Runtime Update owner still performs semantic post-apply verification.

---

### `Maintenance.Restart.Request`

Purpose: request a controlled OS restart only as part of an authorized update/recovery transaction.

Input:

```text
transactionType: UPDATE | RECOVERY
transactionId
restartReasonCode
```

Standalone arbitrary reboot request is prohibited.

Broker MUST verify that referenced transaction context permits restart.

---

## 7. Recovery capabilities

### `Maintenance.Recovery.Execute`

Purpose: execute one predefined recovery strategy step selected by `RecoveryCoordination`.

Input:

```text
recoveryContextId
recoveryActionId
expectedSource/target evidence references
```

`recoveryActionId` MUST map to a release-defined recovery action implementation.

Examples of bounded action classes MAY include:

```text
RESTORE_MANAGED_CONFIG
REPAIR_SPLITOS_COMPONENT
RESTORE_VERIFIED_RELEASE_ARTIFACT
REAPPLY_MACHINE_POLICY
```

Actual action set is finalized with `SPEC-11`.

No arbitrary recovery script field is allowed.

Idempotency: required.  
Cancellation: action-specific.  
Verification: Broker returns technical results/evidence; Recovery owner verifies semantic recovery target.

---

## 8. Capabilities explicitly kept outside Broker

The following SHOULD stay in user session unless a later evidence-based spec proves privilege is required:

```text
Display topology read/apply
Audio endpoint discovery
Input/controller discovery
Game Client launch handoff
Game process correlation
User Game Profile persistence
Account backend HTTPS
Manager/Game Launcher UX
```

Moving such behavior into Broker requires updating trust/privilege analysis.

---

## 9. Capability payload target binding

A privileged target MUST be selected using one of:

1. signed/release-defined stable ID;
2. transaction-owned staged artifact ID;
3. broker-generated job/operation ID;
4. validated machine resource identity already mapped by trusted local configuration.

A target MUST NOT be selected solely by untrusted free-form strings supplied over IPC.

---

## 10. Capability result structure

Mutation results SHOULD expose per-target technical status:

```text
requestedTarget
operationAttempted
immediateResult
actualStateObserved
verificationStatus
errorCode
```

Example:

```text
Service A → stopped → verified
Service B → stop accepted → still running → TARGET_NOT_VERIFIED
```

Batch overall result therefore becomes failure/partial rather than success.

---

## 11. Capability availability

A compiled capability may be temporarily unavailable because:

```text
unsupported Windows build
release/component mismatch
session not control owner
required feature absent
maintenance transaction missing
Broker degraded
```

Availability query MUST distinguish:

```text
NOT_SUPPORTED
TEMPORARILY_UNAVAILABLE
DENIED
READY
```

---

## 12. Capability versioning

Capability identity may include semantic version in the protocol catalog, for example:

```text
Machine.ServicePolicy.Apply@1
```

Breaking payload/behavior change requires a new capability major/version, not silent reinterpretation.

---

## 13. Authorization matrix

| Capability | Valid Runtime Host in console session | Secondary/RDP Host | Manager/Launcher | Idempotency |
|---|---:|---:|---:|---:|
| Broker.Health.Read | yes | yes | no | no |
| Broker.Capabilities.Read | yes | yes | no | no |
| Machine.ManagedState.Read | yes | limited/read policy | no | no |
| Machine.ServicePolicy.Apply | yes | no | no | yes |
| Machine.Policy.Apply | yes | no | no | yes |
| Machine.ManagedComponent.Action | yes | no | no | yes |
| Maintenance.Artifact.VerifyStaged | yes when transaction-authorized | no | no | recommended |
| Maintenance.Update.ApplyVerified | yes when transaction-authorized | no | no | yes |
| Maintenance.Restart.Request | yes when transaction-authorized | no | no | yes |
| Maintenance.Recovery.Execute | yes / recovery-owned context | no parallel session | no | yes |

Recovery/startup implementation may need a machine-owned resume path when no interactive session exists; that path belongs to `SPEC-11` and MUST NOT be implemented by weakening ordinary session authorization.

---

## 14. Forbidden capability aliases

The following are forbidden even under renamed wrappers:

```text
Admin.Execute
System.Run
Shell.Invoke
Script.Apply
Registry.ExecuteOperations
Service.ExecuteOperationsByName
FileSystem.ArbitraryWrite
Process.ArbitraryKill
```

Reviewers MUST reject a capability if its payload can encode effectively arbitrary privileged behavior.

---

## 15. Acceptance criteria

Broker capability implementation conforms only if:

- every privileged operation maps to a catalog ID;
- every mutation target is allowlisted/bound to trusted local metadata;
- mutation requests are idempotent;
- secondary sessions cannot issue machine mutation capabilities;
- UI processes cannot issue Broker capabilities;
- no raw script/command/registry/service/file API exists;
- technical actual-state verification is returned where feasible;
- Update/Recovery semantic commit remains outside Broker.
