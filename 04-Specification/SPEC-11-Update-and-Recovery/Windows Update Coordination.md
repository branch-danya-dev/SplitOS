# Windows Update Coordination

## 1. Purpose

This document defines how SplitOS avoids unsafe overlap with Windows Update while preserving Windows Update as the authority for Microsoft-serviced Windows content.

---

## 2. Non-conflict principle

```text
SplitOS update scheduler
!= Windows Update scheduler
```

SplitOS does not require Windows Update to be disabled.

SplitOS does not assume it can own all restart timing on the machine.

The product instead coordinates by detecting known servicing/reboot conditions, deferring its own apply, persisting safe checkpoints, and reconciling after Windows changes.

---

## 3. Supported observation surface

SplitOS MAY use documented Windows Update Agent APIs as evidence, including:

```text
IUpdateSession
IUpdateSearcher
QueryHistory
installer reboot-required evidence
```

It may additionally consume documented OS/build/version evidence and supported servicing state evidence.

No single evidence source is treated as absolute proof that Windows servicing cannot begin.

---

## 4. WindowsServicingSnapshot

Conceptual snapshot:

```text
snapshotId
observedAtUtc
windowsBuild
ubr/revision evidence
updateSearchState
recentUpdateHistory
rebootRequiredEvidence[]
servicingActivityEvidence[]
compatibilityDecision
confidence
```

The snapshot is ephemeral evidence, not canonical Windows Update state owned by SplitOS.

---

## 5. Pre-apply gate

A SplitOS update that performs machine mutation MUST NOT enter `APPLYING` if any mandatory gate is unresolved:

```text
known Windows servicing active
known Windows update install active
reboot required before safe apply
current Windows base changed since release eligibility evaluation
Windows compatibility = UNKNOWN / REJECTED
```

Result:

```text
DEFERRED_WINDOWS_SERVICING
```

or:

```text
REBOOT_REQUIRED_BEFORE_SPLITOS_UPDATE
```

---

## 6. Mutation lease interaction

The SPEC-05 machine mutation lease serializes SplitOS-owned major mutations:

```text
MODE
UPDATE
RECOVERY
```

It does not claim that SplitOS can lock Microsoft servicing.

Therefore:

```text
SplitOS mutation lease acquired
!= Windows servicing globally locked
```

Update must remain crash/reboot safe even after the lease is acquired.

---

## 7. Unexpected Windows servicing

If fresh evidence indicates Windows servicing began after SplitOS started preparation:

### Before target activation

SplitOS should stop at a safe checkpoint:

```text
staging complete
recovery capsule complete
apply not started
→ defer
```

### During target activation

Update Bootstrap persists its checkpoint and must avoid beginning additional optional mutations.

After Windows servicing/reboot completes:

```text
read UpdateTransaction
→ refresh Windows base/build
→ compatibility evaluation
→ verify source/target activation evidence
→ resume or rollback
```

Blind continuation is forbidden.

---

## 8. Windows update completes first

When Windows Update changes the Windows build/revision:

```text
Windows update
↓
reboot if required
↓
SplitOS startup
↓
refresh Windows compatibility
```

Possible outcomes:

### SUPPORTED

Normal SplitOS runtime may continue.

### SUPPORTED_WITH_KNOWLEDGE_REFRESH

SplitOS may require a signed knowledge/runtime release from its own channel before re-enabling selected managed capabilities.

### UNSUPPORTED / UNKNOWN

Safety target:

```text
Windows Desktop usable
SplitOS managed mutations restricted/disabled
OperationalMode → NONE when safe
Manager surfaces compatibility issue
```

SplitOS must not make Windows login depend on receiving a compatibility update.

---

## 9. SplitOS update completes first

After SplitOS update commit, Windows Update remains free to operate according to Windows/user/admin policy.

SplitOS must not retain temporary update locks/policies that suppress normal Windows servicing.

---

## 10. Restart UX

SplitOS should prefer user-visible coordination over competing restart prompts.

If Windows already requires a restart and SplitOS target also requires one, SplitOS MAY combine its own resume marker with the same next reboot when technically safe.

However:

```text
one reboot
!= one update authority
```

After boot, each subsystem's completion must be verified independently.

---

## 11. Windows Update policy ownership

v1 does not silently impose Windows Update deferral/deadline/active-hours policy.

If a future product feature offers “SplitOS-managed Windows Update timing”, it must be an explicit policy capability using documented Windows policy mechanisms and must be specified separately.

Raw undocumented registry manipulation is not a substitute for such a contract.

---

## 12. Feature updates

Windows feature updates can materially change the component/dependency baseline.

Therefore a feature/build transition requires:

```text
new Windows base evidence
→ Compatibility Management decision
→ Component Matrix / release knowledge validation
```

A previously accepted `REMOVE / DISABLE / MODE_MANAGED / KEEP` decision is not automatically valid on the new Windows build.

---

## 13. Recovery interaction

If Windows Update itself causes Windows-level boot/servicing failure, SplitOS Recovery does not pretend that a previous SplitOS wrapper payload is a Windows image rollback.

SplitOS may:

- enter WinRE-hosted SplitOS Recovery Tool;
- validate/repair SplitOS-owned payloads;
- hand off to Windows-native recovery options;
- revalidate SplitOS after Windows recovery.

It MUST NOT fabricate Windows rollback success from SplitOS capsule restoration.
