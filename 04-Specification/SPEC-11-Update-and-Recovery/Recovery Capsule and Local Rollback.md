# Recovery Capsule and Local Rollback

## 1. Purpose

This document defines the mandatory on-device previous-release backup used to recover SplitOS without reverting personal user data.

---

## 2. Recovery objective

The capsule exists to answer one specific question:

> Can this machine return to the last verified SplitOS software/runtime state if the newly activated SplitOS release fails?

It is not a full-PC backup and not a historical user-file snapshot.

---

## 3. Recovery Store topology

A supported clean installation reserves a dedicated hidden SplitOS Recovery Store on the system device.

Requirements:

```text
no normal drive letter
not used as ordinary user storage
not writable by normal user-session apps
mounted/written only by privileged update/recovery tooling
separate responsibility from Windows RE tools partition
capacity tracked by SplitOS Update Orchestration
```

The Builder/installation layout must provision enough reserve to hold at least one previous-release capsule plus recovery metadata for the supported release class.

Exact capacity is release-defined and must be checked before update activation.

---

## 4. Capsule identity

Conceptual `RecoveryCapsuleRecord`:

```text
capsuleId
sourceReleaseId
createdAtUtc
sealedAtUtc
releaseEnvelopeDigest
capsuleManifestDigest
machineSnapshotDigest
artifactSetDigest
compatibilityFloor
windowsBaseAtCreation
state
verificationResult
```

States:

```text
CREATING
VERIFYING
SEALED_VERIFIED
INVALID
CONSUMED_OR_SUPERSEDED
```

Only `SEALED_VERIFIED` may satisfy the pre-update recovery gate.

---

## 5. Capsule contents

Mandatory categories:

```text
A. Previous release payload
   RuntimeHost / Manager / GameLauncher / Broker / updater-compatible payload

B. Release trust metadata
   previous signed Release Envelope
   artifact digests
   release identifiers

C. Machine rollback metadata
   previous service/task activation mapping
   machine-store snapshot required for product runtime recovery
   update/recovery schema compatibility metadata

D. Baseline compatibility metadata
   InstalledBaselineIdentity / BuildReceipt references
   Windows build compatibility evidence at creation

E. Recovery bootstrap metadata
   recovery-tool compatibility/version requirements
```

---

## 6. Capsule exclusions

The capsule MUST NOT duplicate or roll back personal user state.

Excluded:

```text
%UserProfile% documents
Desktop / Downloads / Pictures / Videos
external app data
external game libraries/saves
browser profiles
Steam/Epic/Microsoft/Battle.net credentials
SplitOS refresh/access tokens as historical backup
```

User data remains live and is protected by compatibility rules, not by replacing it with an older copy.

---

## 7. Sealing

Creation flow:

```text
collect source release payload references
→ collect machine rollback snapshot
→ write capsule manifest
→ hash all required content
→ validate signatures/release identity
→ verify container readability
→ mark SEALED_VERIFIED
```

After seal, ordinary update flow must treat capsule content as immutable.

Mutation of a sealed capsule invalidates it unless a new capsule identity is created and fully re-verified.

---

## 8. One-slot previous release rule

v1 requires one mandatory previous verified release.

Example:

```text
R1 current
update to R2
→ capsule R1
→ R2 current

later update to R3
→ create/verify capsule R2
→ only then may capsule R1 be superseded
→ activate R3
```

At no point during update preparation may the machine have zero valid rollback targets because the updater prematurely deleted the old capsule.

---

## 9. Live rollback

When Windows and the privileged maintenance path remain usable:

```text
Recovery Coordination
→ acquire RECOVERY mutation lease
→ validate capsule
→ stop/quiesce target SplitOS processes
→ restore source release activation metadata
→ restore permitted machine operational snapshot
→ start previous Broker
→ start previous Runtime
→ verify
```

User profile contents remain untouched.

---

## 10. Offline rollback

If live SplitOS runtime cannot recover, WinRE-hosted SplitOS Recovery Tool may:

```text
locate SplitOS Recovery Store
→ validate capsule manifest/signatures/digests
→ inspect current InstalledBaseline/UpdateTransaction evidence
→ restore previous SplitOS release activation/payload
→ repair required SplitOS machine-state files
→ mark recovery continuation
→ reboot to Windows
→ Runtime verifies final state
```

Offline recovery does not claim to repair arbitrary Windows corruption.

---

## 11. Machine-state snapshot scope

Machine operational state may be restored because it belongs to SplitOS runtime correctness.

Candidates include:

```text
machine.db snapshot or rollback-compatible export
service/task activation metadata
release-owned machine policy catalog version
update/recovery journal references
```

Restoration MUST be schema/version validated.

The snapshot MUST NOT restore external Windows/user application state simply because it is convenient.

---

## 12. Capsule validation before use

Every rollback use re-validates:

```text
container readable
manifest integrity
artifact digests
release trust/signature state
Windows compatibility floor
required recovery tool version
machine identity/installation binding if specified
```

A capsule that was valid when created can become unusable after a material Windows base change.

Result:

```text
CAPSULE_VALID
CAPSULE_WINDOWS_INCOMPATIBLE
CAPSULE_CORRUPTED
CAPSULE_SIGNATURE_REJECTED
CAPSULE_SCHEMA_UNSUPPORTED
```

Recovery must not apply a known-incompatible capsule blindly.

---

## 13. Windows base change consequence

Suppose:

```text
SplitOS R5 + Windows Build A
→ capsule R5
→ Windows feature update → Build B
```

If R5 is not compatible with Build B, capsule R5 is not a valid automatic rollback target for managed runtime on Build B.

SplitOS may preserve it for forensic/manual recovery evidence, but must not automatically activate an incompatible release.

This is why SplitOS update compatibility and Windows Update coordination are linked.

---

## 14. Disk failure limitation

Because the recovery store lives on the same physical device:

```text
drive failure
controller failure
full-device loss
catastrophic media corruption
```

may destroy both live installation and capsule.

The product must describe this honestly.

External/cloud backup is a separate future capability.

---

## 15. Security boundary

Normal users may be allowed to view capsule status through Manager, but they do not receive arbitrary mount/write access.

Only trusted maintenance/recovery code may modify the recovery store.

Exact key hierarchy and signature revocation behavior is defined in SPEC-12.
