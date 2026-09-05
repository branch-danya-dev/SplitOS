# Recovery Environment and Escalation

## 1. Purpose

This document defines the offline recovery boundary and the escalation order used when normal SplitOS runtime recovery is insufficient.

---

## 2. Windows RE remains base recovery authority

SplitOS does not replace Windows Recovery Environment.

Supported topology:

```text
Windows RE
├── Microsoft recovery/repair tools
└── SplitOS Recovery Tool     custom tool / product extension

SplitOS Recovery Store
└── previous-release capsule
```

The SplitOS tool is an extension hosted from a supported WinRE customization path, not a custom bootloader pretending to be Windows recovery.

---

## 3. Why the Recovery Store is separate

Windows RE tooling itself may be serviced/resized by Windows.

Therefore:

```text
WinRE partition
→ recovery execution environment

SplitOS Recovery Store
→ product rollback payload/data
```

Keeping these responsibilities separate reduces the chance that ordinary WinRE maintenance becomes deletion/repacking of the SplitOS last-known-good release.

---

## 4. Entry paths

Recovery may be entered through:

```text
Manager → Restore previous SplitOS version
automatic Recovery Coordination → reboot into maintenance path
Windows Advanced Startup → SplitOS Recovery Tool
manual WinRE invocation when normal runtime cannot start
```

The exact boot-menu UX may be refined in implementation, but no recovery path may bypass capsule validation.

---

## 5. SplitOS Recovery Tool capabilities

Allowed semantic operations:

```text
InspectCurrentSplitOSState
ValidateRecoveryCapsule
RestorePreviousSplitOSRelease
RepairSplitOSActivationMetadata
InspectUpdateTransaction
ExportDiagnostics
ReturnToWindowsRecovery
```

Forbidden generic capabilities:

```text
RunArbitraryCommand
ExecuteArbitraryBinary
EditAnyRegistryPath
DeleteArbitraryFile
```

---

## 6. Offline evidence

The tool reads:

```text
Windows installation identity/build
SplitOS machine store / transaction records
service/task activation metadata
current release roots
Recovery Store capsule
BuildReceipt / InstalledBaseline references
```

It must distinguish stale persisted metadata from actual offline filesystem/configuration evidence.

---

## 7. Previous-release restore

Offline restore sequence:

```text
validate Windows install
→ validate capsule against current Windows compatibility
→ validate capsule signature/digests
→ validate machine binding when applicable
→ create Recovery transaction record
→ restore previous SplitOS release root/activation metadata
→ restore permitted machine-state snapshot
→ preserve user data/secrets
→ write reboot continuation marker
→ reboot
→ online Runtime verifies final state
```

The offline tool does not declare success before the online verification phase when that phase is required.

---

## 8. If Windows itself is broken

A valid SplitOS previous-release capsule may not repair:

```text
Windows boot files
corrupt Windows component store
failed Microsoft feature update
storage filesystem corruption
broken boot-critical driver
```

In such cases SplitOS exposes/returns to Windows-native repair capabilities.

It may preserve/export its own diagnostics and capsule but MUST NOT report `SPLITOS_RECOVERED` if Windows cannot boot to the required supported baseline.

---

## 9. Destructive recovery

SplitOS MUST NOT automatically trigger:

```text
Reset this PC
clean Windows reinstall
partition format
user-profile deletion
```

If all non-destructive recovery paths are exhausted, Manager/WinRE may explain that Windows-level recovery is required and present supported Windows recovery options.

A destructive operation requires explicit user action through the appropriate Windows/recovery UX.

---

## 10. User-data promise boundary

Normal SplitOS rollback is designed to preserve user data because it restores only product/runtime state.

However the product must not promise protection from:

```text
physical disk failure
full-device loss
user-selected destructive Windows reset without backup
unrecoverable filesystem corruption affecting user data
```

Those require external backup strategy beyond same-device SplitOS recovery.

---

## 11. Recovery priority

Every recovery decision follows:

```text
1. preserve user data
2. preserve/restore Windows bootability
3. establish coherent installed baseline
4. establish correct SplitOS release/runtime
5. restore operational mode/profile context
6. restore UI convenience
```

A system that boots normal Windows with SplitOS managed runtime temporarily disabled is preferable to a more aggressive recovery that risks user data or Windows bootability.

---

## 12. Recovery completion

Completion requires:

```text
Windows boots
current InstalledSplitOSRelease known
Broker health verified or intentionally disabled safe
Runtime health verified or intentionally disabled safe
machine DB valid
no ambiguous mutation owner
user data store preserved
recovery journal committed
```

Mode may remain `NONE` after recovery until the next safe activation.
