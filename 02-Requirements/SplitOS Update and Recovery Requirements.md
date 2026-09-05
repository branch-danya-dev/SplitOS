# SplitOS — Update and Recovery Requirements

## 1. Purpose

This document supplements the existing `FR-UPDATE-*` and `FR-RECOVERY-*` requirements with the clarified product requirement for:

- an independent SplitOS-owned update channel for the SplitOS wrapper/runtime;
- controlled coexistence with Microsoft Windows servicing;
- automatic preservation of the previous SplitOS release on the same device;
- recovery that preserves user data instead of restoring the user's profile to an older point in time.

This supplement does not change the core principle already present in `FR-UPDATE-001..009`: Windows patches remain externally sourced Microsoft changes and are not considered supported until SplitOS compatibility validation.

---

## 2. Windows servicing clarification

The following interpretation is canonical:

```text
Microsoft
→ authority/source for Windows patch payload

SplitOS Compatibility
→ authority for whether a Windows patch/build is supported by current SplitOS release

SplitOS Update Channel
→ authority/source for SplitOS-owned wrapper/runtime/knowledge payload
```

Disabling automatic application of unvalidated Windows feature/system updates does **not** imply removing or replacing the Windows Update servicing infrastructure.

A SplitOS release may carry compatibility/approval metadata for a Microsoft patch while the patch payload itself is still obtained/applied through supported Microsoft servicing mechanisms.

---

# 3. SplitOS Update Channel

## FR-UPDATE-010

SplitOS MUST provide an independent update delivery channel for SplitOS-owned runtime, Manager, Game Launcher, Broker, adapters, product knowledge and recovery tooling.

## FR-UPDATE-011

The SplitOS-owned update channel MUST remain logically separate from the Microsoft Windows update payload channel.

## FR-UPDATE-012

SplitOS MUST NOT require rehosting or repackaging Microsoft Windows quality/feature/security update binaries inside the SplitOS wrapper update channel.

## FR-UPDATE-013

SplitOS MUST be able to distribute signed compatibility metadata that declares which Windows build/patch combinations are supported by a SplitOS release.

## FR-UPDATE-014

SplitOS MUST prevent its own machine-mutating update flow from knowingly running concurrently with a conflicting Windows servicing/update operation.

## FR-UPDATE-015

A SplitOS update MUST NOT be considered installed successfully until the target SplitOS release is activated, verified and durably committed.

## FR-UPDATE-016

SplitOS update preparation MUST verify that sufficient local recovery capacity exists for the mandatory previous-release backup before activating the target release.

## FR-UPDATE-017

If the mandatory previous-release backup cannot be created and verified, automatic activation of the target SplitOS release MUST NOT proceed.

---

# 4. Previous-release local recovery

## FR-RECOVERY-008

Before activating a new SplitOS release, the system MUST automatically preserve the currently verified SplitOS release as a local rollback target on the same device.

## FR-RECOVERY-009

The previous-release rollback target MUST be stored in a dedicated isolated recovery area that is not used as ordinary user storage and is not normally exposed with a drive letter.

## FR-RECOVERY-010

The local recovery target MUST contain the SplitOS-owned software/runtime and machine recovery metadata necessary to restore the previous SplitOS release.

## FR-RECOVERY-011

SplitOS software rollback MUST NOT restore ordinary user files or canonical user preferences from an older point-in-time snapshot when doing so would discard changes made after the update.

## FR-RECOVERY-012

SplitOS update/migration design MUST preserve at least one-release rollback compatibility for canonical user data, or provide a tested rollback bridge that preserves the user's current data.

## FR-RECOVERY-013

When normal SplitOS runtime cannot start, the user MUST have access to an offline SplitOS recovery path integrated with or reachable from the Windows recovery environment.

## FR-RECOVERY-014

Recovery MUST prioritize user-data integrity and Windows bootability above restoration of premium/managed SplitOS functionality.

## FR-RECOVERY-015

SplitOS MUST NOT automatically perform destructive Windows reset/reinstallation as part of normal previous-release rollback.

## FR-RECOVERY-016

The product MUST clearly distinguish same-device SplitOS recovery from a backup that protects against physical disk/device loss.

---

# 5. User-visible outcomes

The Manager/recovery UX MUST be able to communicate at least:

```text
SplitOS update available
SplitOS update staged
Windows update/servicing conflict — SplitOS update deferred
restart required before/for update
previous SplitOS recovery version available
SplitOS update failed — previous release restored
SplitOS runtime unavailable — Windows remains usable
local recovery unavailable — external/Windows recovery required
```

---

# 6. Traceability

```text
Product clarification: independent wrapper update channel
→ FR-UPDATE-010..017
→ SPEC-11 SplitOS Update Channel / Windows Update Coordination / Update Transaction

Product clarification: automatic previous-version backup on device
→ FR-RECOVERY-008..016
→ SPEC-11 Recovery Capsule / User Data Protection / WinRE Recovery
```
