# Windows Update Coordination

## 1. Purpose

This document defines how SplitOS coordinates its own update lifecycle with Microsoft Windows servicing while preserving the existing product requirement that unvalidated Windows feature/system updates are not automatically applied.

---

## 2. Authority model

```text
Microsoft
→ source/issuer of Windows quality/feature/security update payloads

SplitOS Compatibility
→ decides whether a Windows patch/build is supported by SplitOS

SplitOS Update Channel
→ distributes SplitOS-owned wrapper/runtime/knowledge payloads
```

The SplitOS product does not replace the Microsoft servicing stack.

The SplitOS product **does** control automatic eligibility/timing of Windows changes according to `FR-UPDATE-001..009` so an unvalidated Windows build does not silently mutate the supported baseline.

---

## 3. Windows servicing lane

Approved Windows patching uses supported Microsoft servicing mechanisms.

Conceptually:

```text
SplitOS Knowledge Release
→ approved Windows KB/build metadata
→ Windows Update Agent / supported Windows servicing source
→ Microsoft-signed Windows payload
→ install
→ reboot if required
→ SplitOS compatibility + baseline verification
```

The SplitOS wrapper channel MUST NOT rehost/re-sign Microsoft patch binaries.

---

## 4. Automatic update policy

v1 baseline preserves the existing requirement:

```text
unvalidated automatic Windows feature/system update
→ NOT ALLOWED
```

This means SplitOS may configure supported Windows update policy/timing so unknown changes are not automatically committed before compatibility validation.

It does **not** mean:

```text
remove Windows Update service
replace Microsoft servicing engine
block all security servicing forever
use undocumented registry/service hacks
```

Exact Windows policy mechanism must use documented supported policy/WUA surfaces for the supported edition/build.

---

## 5. Supported observation/control evidence

SplitOS MAY use documented Windows Update Agent APIs such as:

```text
IUpdateSession
IUpdateSearcher
IUpdateDownloader
IUpdateInstaller
QueryHistory
reboot-required evidence
```

The Windows servicing lane may use these APIs to search/download/install an already-approved Microsoft update where supported.

SplitOS may also consume documented OS/build/version and servicing evidence.

No single API response is treated as total proof that Windows servicing cannot begin or that a patch is semantically compatible with SplitOS.

---

## 6. WindowsServicingSnapshot

Conceptual evidence object:

```text
snapshotId
observedAtUtc
windowsBuild
ubr/revision evidence
pendingApprovedUpdates[]
activeServicingEvidence[]
recentUpdateHistory[]
rebootRequiredEvidence[]
compatibilityDecision
approvedPatchSetVersion
confidence
```

This snapshot is evidence only. Microsoft/Windows remain authority for actual servicing state.

---

## 7. Windows patch approval

A Windows patch/build is accepted only after SplitOS compatibility validation.

Approval metadata may be delivered through a SplitOS `KNOWLEDGE_RELEASE`:

```text
WindowsPatchApproval
├── target Windows edition/build family
├── KB/update identity
├── minimum SplitOS release
├── compatibility status
├── known constraints
├── Component Matrix implications
└── validation evidence/version
```

Then:

```text
approved metadata
!= Windows patch payload
```

The payload still comes from the supported Microsoft servicing channel.

---

## 8. Pre-apply gate for SplitOS wrapper update

A SplitOS update that performs machine mutation MUST NOT enter `APPLYING` while conflicting Windows servicing is known or unresolved:

```text
Windows servicing active
approved Windows update install active
reboot required before safe SplitOS apply
Windows build changed since eligibility evaluation
Windows compatibility UNKNOWN / REJECTED
Recovery already active
Mode transition owns machine mutation lease
another SplitOS update owns mutation lease
```

Typed outcomes:

```text
DEFERRED_WINDOWS_SERVICING
REBOOT_REQUIRED_BEFORE_SPLITOS_UPDATE
WINDOWS_COMPATIBILITY_REEVALUATION_REQUIRED
```

---

## 9. Pre-apply gate for Windows patch

The Windows servicing lane likewise MUST NOT start a validated Windows patch while a conflicting SplitOS major mutation is active:

```text
MODE transition active
SplitOS UPDATE applying
RECOVERY active
```

The system should converge to a stable mode/state first.

---

## 10. Mutation lease scope

The SPEC-05 mutation lease serializes SplitOS-owned major mutations:

```text
MODE
UPDATE
RECOVERY
```

It cannot globally lock Microsoft servicing.

Therefore:

```text
SplitOS mutation lease acquired
!= Windows servicing physically impossible
```

Both update paths remain crash/reboot safe and re-check fresh evidence before machine-visible operations.

---

## 11. Unexpected servicing overlap

If Windows servicing begins unexpectedly while SplitOS has prepared but not activated its target release:

```text
staging complete
capsule complete
apply not started
→ defer
```

If overlap occurs after target activation began:

```text
persist current update checkpoint
→ stop optional mutations
→ survive/reconcile reboot
→ refresh Windows build/servicing evidence
→ compatibility evaluation
→ resume or rollback
```

Blind continuation is forbidden.

---

## 12. Windows patch completion

After a validated Windows patch is installed:

```text
reboot if required
→ read actual Windows build/revision
→ verify expected patch state
→ rerun SplitOS compatibility predicates
→ refresh Component Matrix/runtime integration evidence
```

Possible outcomes:

### SUPPORTED

Normal SplitOS managed runtime continues.

### SUPPORTED_WITH_SPLITOS_UPDATE_REQUIRED

Windows remains usable; selected managed SplitOS capabilities may be restricted until a compatible SplitOS release/knowledge package is installed.

### UNKNOWN / UNSUPPORTED

Safety target:

```text
Windows Desktop usable
ManagedRuntime restricted/disabled when safe
OperationalMode → NONE
Manager explains compatibility state
```

Windows login never depends on receiving the SplitOS update.

---

## 13. Restart UX

If an approved Windows patch and a SplitOS update both require restart, the UI MAY coordinate them into one user-visible maintenance window when their transaction checkpoints prove that this is safe.

But:

```text
one physical reboot
!= one semantic transaction
```

After boot, Windows patch state and SplitOS release state are verified independently.

---

## 14. Feature updates

Windows feature/build updates have higher compatibility risk than ordinary revision changes.

Before a feature/build transition is approved:

```text
new Windows base candidate
→ Component Matrix revalidation
→ boot/OOBE/recovery test
→ Runtime integration test
→ game/client compatibility test
→ SplitOS compatibility release
```

An old component classification is not automatically valid on a new Windows build.

---

## 15. Recovery interaction

If an approved Microsoft update causes Windows-level boot/servicing failure, a SplitOS previous-wrapper capsule is not a Windows image backup.

SplitOS may:

- use WinRE-hosted SplitOS Recovery Tool to inspect/repair SplitOS-owned state;
- preserve diagnostics;
- hand off to Windows-native update rollback/repair where supported;
- revalidate/recover SplitOS after Windows is stable.

It MUST NOT report Windows rollback success merely because the SplitOS wrapper was restored.
