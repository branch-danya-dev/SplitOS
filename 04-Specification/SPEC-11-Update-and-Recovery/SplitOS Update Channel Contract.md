# SplitOS Update Channel Contract

## 1. Purpose

This document defines the independent SplitOS release channel used to deliver SplitOS-owned software and knowledge without conflating that channel with Microsoft Windows Update.

---

## 2. Authority split

```text
Windows Update
→ Microsoft-serviced Windows payloads

SplitOS Update Channel
→ SplitOS-owned release payloads
```

SplitOS MUST NOT:

- publish Microsoft Windows updates as SplitOS artifacts;
- disable Windows Update in order to make the SplitOS channel work;
- install a fake/undocumented Windows Update provider;
- treat Windows Update history as SplitOS release truth.

SplitOS MAY present both channels in one Manager UX, but the UI must preserve separate authority and status.

---

## 3. Channel topology

```text
SplitOS Release Authority
↓
published Release Envelope
↓
HTTPS release metadata endpoint / CDN
↓
Runtime Host Update Orchestration
↓
cryptographic verification
↓
local staging
```

HTTPS protects transport. It is not sufficient release trust by itself.

Every accepted release MUST also pass product signature and digest validation defined here and refined by SPEC-12.

---

## 4. Release Envelope

Conceptual `SplitOSReleaseEnvelope`:

```text
schemaVersion
releaseId
semanticVersion
channel
publishedAt
releaseClass
minimumCurrentRelease
supportedWindowsBase[]
requiredCapabilities[]
rebootPolicy
rollbackCompatibility
requiredStagingBytes
requiredRecoveryReserveBytes
artifacts[]
knowledgePackages[]
migrationContract
releaseNotesRef
signingKeyId
signature
```

Artifact entry:

```text
artifactId
type
version
sizeBytes
sha256
contentUri
required
activationPhase
```

The client MUST NOT accept a URI/artifact that is not bound to the signed envelope.

---

## 5. Release classes

### RUNTIME_RELEASE

May contain:

```text
RuntimeHost
Manager
GameLauncher
Broker
UpdateBootstrap
owned libraries
client adapters
```

### KNOWLEDGE_RELEASE

May contain versioned product knowledge such as:

```text
Game Client capability metadata
Game configuration knowledge
optimization ladders
compatibility catalogs
typed mode/service/policy catalogs
```

Knowledge-only release still requires signature/version/compatibility validation.

### BASELINE_MAINTENANCE

Represents SplitOS-owned maintenance of installed baseline configuration.

It MUST NOT package Microsoft quality/feature/security update payloads.

Because it may mutate machine-wide component/configuration state, it requires the UPDATE mutation lease and a verified recovery capsule.

### RECOVERY_TOOL_RELEASE

Updates SplitOS recovery tooling compatible with current WinRE/Windows baseline.

Recovery tooling must be updated in a way that never destroys the only currently usable recovery path.

---

## 6. Channels

v1 production MUST implement:

```text
STABLE
```

Optional future channels:

```text
PREVIEW
DEV
```

A non-STABLE channel MUST be explicit opt-in and MUST use the same trust chain and artifact verification requirements.

Channel selection cannot bypass:

```text
signature
compatibility
recovery reserve
rollback compatibility
```

---

## 7. Discovery

Update discovery is read-only.

Result examples:

```text
NO_UPDATE
UPDATE_AVAILABLE
UPDATE_AVAILABLE_REBOOT_REQUIRED_FIRST
CURRENT_WINDOWS_UNSUPPORTED
CURRENT_RELEASE_UNSUPPORTED
ROLLOUT_NOT_ELIGIBLE
CHANNEL_UNAVAILABLE
```

Discovery MUST NOT mutate installed release state.

---

## 8. Eligibility

Before download/apply, Update Orchestration evaluates:

```text
current SplitOS release
current Windows base/build
current entitlement if update class is entitlement-gated
release channel
known compatibility decision
available staging space
available recovery reserve
current machine mutation owner
Windows servicing/reboot evidence
rollback compatibility
```

An update may be downloadable but not currently applicable.

```text
downloadable != eligible-to-activate
```

---

## 9. Rollout

The release service MAY use deterministic rollout/rings.

Rollout metadata MUST be server-issued and release-scoped. The local client MUST NOT fabricate rollout eligibility.

Emergency revocation / mandatory security semantics are specified with key/revocation policy in SPEC-12.

---

## 10. Download/staging

Artifacts are downloaded only into a transaction-scoped staging area, for example:

```text
%ProgramData%\SplitOS\Maintenance\Updates\<UpdateTransactionId>\Staging\
```

Downloaded bytes are not executable authority.

```text
download
→ size check
→ digest verification
→ signature/release-envelope verification
→ compatibility check
→ STAGED_VERIFIED
```

Unverified content MUST NOT be passed into privileged apply operations.

---

## 11. Side-by-side release staging

Target runtime payload SHOULD be assembled into a versioned release root before activation:

```text
C:\Program Files\SplitOS\Releases\<releaseId>\
```

Staging a target side-by-side allows current release processes to remain coherent until the privileged activation window.

The canonical installed release remains the source release until target verification and commit.

---

## 12. Update Bootstrap

`SplitOS.UpdateBootstrap.exe` is a fixed-purpose one-shot maintenance process.

It may be launched only through the Broker's typed `Maintenance.Update.ApplyVerified` capability.

The Runtime Host cannot provide an arbitrary executable path.

Bootstrap inputs are limited to trusted transaction identity and release-owned activation metadata.

Bootstrap may:

- quiesce SplitOS runtime processes;
- stop/start the SplitOS Broker service when required;
- switch service/task activation metadata to a staged release;
- execute release-defined typed migrations;
- persist activation checkpoints;
- restart/verify required SplitOS components;
- restore the previous activation metadata during rollback.

It MUST NOT expose a generic command shell.

---

## 13. Commit rule

Canonical installed-release identity changes only after:

```text
target release activation
+
required process/service health
+
required machine-store migration
+
required compatibility predicates
+
post-activation read-back
```

Then:

```text
CommitInstalledSplitOSRelease(targetReleaseId)
```

Before this commit, the source release remains canonical even if some target processes have already started.

---

## 14. Failure semantics

Examples:

```text
RELEASE_ENVELOPE_INVALID
ARTIFACT_DIGEST_MISMATCH
SIGNATURE_INVALID
WINDOWS_BASE_NOT_SUPPORTED
RECOVERY_RESERVE_INSUFFICIENT
ROLLBACK_COMPATIBILITY_NOT_PROVEN
WINDOWS_SERVICING_CONFLICT
STAGING_FAILED
ACTIVATION_FAILED
TARGET_HEALTH_NOT_REACHED
```

A failed target must not silently overwrite the current release identity.
