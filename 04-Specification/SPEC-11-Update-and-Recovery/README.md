# SPEC-11 — Update & Recovery

## Purpose

SPEC-11 defines how an installed SplitOS baseline receives SplitOS-owned updates, coordinates safely with Microsoft Windows Update, preserves a local last-known-good SplitOS release, resumes after reboot/crash, and restores a coherent runtime without rolling back user data.

The package preserves the existing ownership rule:

```text
Microsoft Windows Update
→ Windows / Microsoft-serviced OS payload authority

SplitOS Update Channel
→ SplitOS-owned runtime / policy / knowledge / recovery payload authority
```

The two channels MUST remain separate. SplitOS MUST NOT register an undocumented Windows Update provider, replace Windows Update, disable Windows Update as a product invariant, or route Microsoft OS patches through the SplitOS release feed.

---

## Core update model

```text
SplitOS Release Service / CDN
        ↓ HTTPS transport
signed SplitOS Release Envelope
        ↓
Runtime Host / Update Orchestration
        ↓
eligibility + Windows-servicing conflict check
        ↓
download + cryptographic verification
        ↓
stage target release
        ↓
create + verify previous-release Recovery Capsule
        ↓
acquire UPDATE machine-mutation lease
        ↓
one-shot privileged Update Bootstrap
        ↓
activate target release
        ↓
read actual state + health verification
        ↓
commit InstalledSplitOSRelease
```

Critical invariant:

```text
release downloaded
!= release trusted
!= release staged
!= release activated
!= release verified
!= release committed
```

---

## SplitOS Update Channel

The SplitOS channel distributes only SplitOS-owned artifacts.

Supported semantic update classes:

```text
RUNTIME_RELEASE
→ Runtime Host / Manager / Game Launcher / Broker payload and owned libraries

KNOWLEDGE_RELEASE
→ compatibility knowledge, Game Client capability data, Game Profile optimization knowledge,
   typed policy catalogs and other release-owned product knowledge

BASELINE_MAINTENANCE
→ validated SplitOS-owned changes to installed baseline configuration/component state;
   does not replace Microsoft Windows servicing authority

RECOVERY_TOOL_RELEASE
→ SplitOS recovery/bootstrap tooling compatible with current Windows/WinRE baseline
```

`STABLE` is the mandatory v1 production channel. Additional preview/test channels MAY exist later, but changing channel MUST be an explicit user/admin action and MUST NOT weaken signature or compatibility validation.

---

## Windows Update coexistence

Windows Update remains responsible for Windows quality/feature/security updates and Microsoft-serviced drivers/packages.

Before applying a SplitOS mutation, Update Orchestration MUST obtain fresh Windows servicing evidence and MUST defer when any of the following is materially true:

```text
Windows update installation is active
Windows servicing transaction is active
Windows reboot is required before safe SplitOS apply
Windows build changed and SplitOS compatibility has not been re-evaluated
Recovery is already active
another SplitOS UPDATE owns the mutation window
Mode transition owns the mutation window
```

SplitOS SHOULD observe supported Windows Update Agent/history/reboot evidence, but MUST NOT assume that one API call gives complete authority over Windows servicing state.

If Windows begins servicing unexpectedly during a SplitOS transaction, SplitOS MUST stop at the next safe checkpoint and either resume after reconciliation or roll back; it MUST NOT race Windows servicing with blind machine mutations.

---

## Local previous-release protection

Before a target SplitOS release may activate, the currently committed release MUST be preserved as a verified local recovery target.

v1 requires one mandatory last-known-good previous release:

```text
Current verified release N
        ↓ update to N+1
create + verify capsule for N
        ↓
activate + verify N+1
        ↓
keep capsule N available
```

When later updating `N+1 → N+2`, the mandatory previous-release slot becomes `N+1` only after the new capsule is created and verified.

No automatic update may consume the only valid rollback target before the replacement capsule is verified.

---

## SplitOS Recovery Store

A clean SplitOS installation MUST reserve a dedicated hidden SplitOS recovery store on the system device.

Target v1 topology:

```text
Physical device
│
├── EFI / Windows system partitions
├── Windows OS / user data volume
├── Windows RE tools partition
└── SplitOS Recovery Store        hidden, no normal drive letter
      └── previous-release Recovery Capsule
```

The SplitOS Recovery Store is intentionally separate from the ordinary Windows data path and SHOULD be separate from the Windows RE tools partition so Windows RE servicing/resizing does not become the product payload store.

The store is not a substitute for an external backup. A same-device recovery store cannot protect against physical disk loss or total-device destruction.

---

## Recovery Capsule

`SplitOSRecoveryCapsule` is an immutable, integrity-verified container for one previous SplitOS release.

It contains only rollback-relevant SplitOS-owned state, for example:

```text
previous SplitOS release payloads
previous signed Release Envelope / artifact digests
previous machine-level SplitOS state snapshot
service/task/bootstrap activation metadata
schema/migration compatibility metadata
BuildReceipt / InstalledBaseline compatibility references
recovery tool compatibility metadata
```

It MUST NOT be a snapshot of the user's Windows profile.

It MUST NOT contain or roll back:

```text
Documents / Desktop / Downloads
external game saves
external application data
Steam/Epic/Xbox/Battle.net account data
ordinary Windows user files
reusable SplitOS account tokens as old-version backup copies
```

The exact physical capsule file format is an implementation detail, but the v1 security contract requires a signed manifest, content digests, immutable-after-seal behavior, recovery-store isolation, and verification before activation of the target release.

---

## User data preservation

Recovery of SplitOS software MUST NOT equal rollback of user data.

Canonical rule:

```text
software rollback
!= user-data rollback
```

Per-user canonical data such as Game Profiles and preferences remains in the live user store. A new release may not perform a destructive schema migration that makes the mandatory previous release unable to preserve/read the existing data unless a tested rollback bridge exists.

v1 therefore requires at least one-release rollback compatibility for canonical user data.

Machine operational state may have a capsule snapshot because restoring machine orchestration state does not represent restoring the user's personal files to an older point in time.

---

## Update transaction states

Normative transaction lifecycle:

```text
DISCOVERED
→ ELIGIBILITY_CHECK
→ DOWNLOADING
→ VERIFYING_ARTIFACTS
→ STAGING
→ PREPARING_RECOVERY
→ READY_TO_APPLY
→ APPLYING
→ [AWAITING_REBOOT]
→ RESUMING
→ VERIFYING_TARGET
→ COMMITTING
→ COMPLETED
```

Failure paths may enter:

```text
ROLLING_BACK
RECOVERING
FAILED_WITH_SAFE_FALLBACK
```

`COMPLETED` is not reached until target health and installed-release identity are verified and durably committed.

---

## Privileged apply model

The Runtime Host remains the semantic Update Orchestration owner.

It does not replace privileged files directly.

```text
Runtime Host
→ verified update plan
→ Broker capability Maintenance.Update.ApplyVerified
→ fixed trusted one-shot SplitOS.UpdateBootstrap.exe
→ stop/quiesce SplitOS processes
→ activate staged release
→ restart Broker/Runtime
→ health handshake
```

The bootstrap executable MUST be selected by release-owned trusted metadata, not by an arbitrary caller path.

Target release payloads SHOULD be staged side-by-side under a versioned release root before activation so locked binaries are not modified in place during the ordinary prepare phase.

---

## Reboot and crash rules

An update transaction is durable before any irreversible/machine-visible mutation.

On startup:

```text
incomplete UpdateTransaction detected
↓
commit durable?
├── NO  → current/source release remains canonical; reconcile / rollback
├── YES → target release is canonical; verify actual activation and repair if needed
└── UNKNOWN → Recovery; never guess
```

A reboot requested by the update is a continuation point, not success.

```text
reboot occurred
!= update completed
```

---

## Recovery levels

Recovery escalates in order:

```text
1. Retry non-destructive verification
2. Resume staged transaction
3. Live rollback to previous verified SplitOS release
4. Recovery-bootstrap rollback using SplitOS Recovery Store
5. WinRE-hosted SplitOS Recovery Tool
6. Windows-native repair / recovery handoff
7. destructive reinstall/reset only as explicit last resort
```

No destructive Windows reset/reinstall is triggered automatically by SplitOS.

Recovery priority remains:

```text
User data integrity
→ Windows bootability/usability
→ coherent installed baseline
→ SplitOS runtime recovery
→ mode/profile restoration
→ UX convenience
```

---

## Automatic rollback

Automatic rollback is allowed when SplitOS can prove that the target release cannot reach mandatory runtime health while the previous capsule remains valid.

Examples:

```text
Broker cannot start/handshake
Runtime Host repeatedly fails target startup verification
required target package digest/activation mismatch
post-reboot target verification fails
critical machine-store migration cannot complete safely
```

Exact retry counts/time windows are deferred to SPEC-14 verification policy and MUST NOT be guessed by implementation.

---

## Manual recovery

The user MAY explicitly request:

```text
Restore previous SplitOS version
```

from Manager when normal runtime is available, or from the SplitOS Recovery Tool in WinRE when it is not.

The UI MUST disclose that the operation restores SplitOS software/runtime state, not personal files to an earlier date.

---

## Non-goals

SPEC-11 does not define:

- release signing key hierarchy/revocation — SPEC-12;
- detailed telemetry/log retention — SPEC-13;
- exact retry thresholds/acceptance test cases — SPEC-14;
- cloud/user-document backup;
- protection from physical disk loss;
- undocumented Windows Update suppression or replacement;
- automatic rollback of Microsoft Windows quality/feature updates through SplitOS packages.

---

## Decisions

```text
SPEC-DEC-109  SplitOS uses an independent signed update channel for SplitOS-owned artifacts.
SPEC-DEC-110  Windows Update remains authority for Microsoft-serviced Windows payloads.
SPEC-DEC-111  SplitOS does not register an undocumented Windows Update provider or disable Windows Update as a product invariant.
SPEC-DEC-112  Every update uses a signed release envelope plus per-artifact digests.
SPEC-DEC-113  Target SplitOS release is staged before privileged activation.
SPEC-DEC-114  Update uses the shared major machine-mutation lease with fencing.
SPEC-DEC-115  One verified previous SplitOS release is a mandatory local rollback target.
SPEC-DEC-116  Previous-release capsule must be created and verified before target activation.
SPEC-DEC-117  SplitOS Recovery Store is hidden and isolated from ordinary live user-data paths.
SPEC-DEC-118  Windows RE and SplitOS Recovery Store remain distinct responsibilities.
SPEC-DEC-119  Software rollback MUST NOT roll back personal/user canonical data.
SPEC-DEC-120  Canonical user-data schema supports at least one previous release or a tested rollback bridge.
SPEC-DEC-121  Machine operational state may be snapshotted/restored as part of a recovery capsule.
SPEC-DEC-122  Update reboot is a continuation point, not completion.
SPEC-DEC-123  Target installed release commits only after mandatory post-activation verification.
SPEC-DEC-124  WinRE may host a SplitOS custom recovery tool without replacing Windows RE.
SPEC-DEC-125  Automatic destructive Windows reset/reinstall is forbidden.
SPEC-DEC-126  Same-device capsule is recovery, not protection against physical disk loss.
```
