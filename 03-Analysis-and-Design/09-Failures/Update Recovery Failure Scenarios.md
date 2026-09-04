# SplitOS — Update & Recovery Failure Scenarios

## 1. Purpose

Документ описывает failures для update, reboot-resume и recovery lifecycle.

Основной принцип:

```text
Update target
!= canonical installed baseline
until apply + read-back + verification succeed.
```

---

# 2. Update preparation failures

## UF-01 — Update entitlement not available

Controlled outcome.

```text
Update transaction not started
InstalledBaselineIdentity unchanged
```

Windows remains usable.

---

## UF-02 — Update is not compatibility-approved

Compatibility Management rejects candidate release/update.

Response:

```text
DEFER / BLOCK
```

Microsoft release availability alone does not make the update SplitOS-supported.

---

## UF-03 — Major mutation already active

Examples:

```text
Work→Game transition active
Recovery active
```

Update must not begin conflicting machine mutation.

Response:

```text
DEFER
```

---

## UF-04 — Update transaction durability unavailable

SplitOS cannot persist enough transaction state to resume safely after reboot/crash.

Response:

```text
DO NOT ENTER APPLYING
```

This is blocking for reboot-capable update flows.

---

# 3. Staging / apply failures

## UF-10 — Package/download/staging failed

If machine mutation has not begun:

```text
UpdateTransaction = FAILED_PRE_APPLY
InstalledBaselineIdentity unchanged
normal runtime remains available
```

Retry may be allowed according to bounded policy.

---

## UF-11 — Apply operation fails before reboot

### Rule

Immediate installer/tool failure does not prove current machine state is unchanged.

Response:

1. Read actual relevant system/update state.
2. Determine whether changes were partially applied.
3. If previous baseline remains coherent, end as failed with old baseline canonical.
4. If current state is uncertain/partial, escalate to Recovery Coordination.

---

## UF-12 — Mandatory SplitOS update component failed, optional component succeeded

Classification:

```text
PARTIAL_APPLICATION
```

Target release cannot be committed.

Response:

```text
rollback / recovery
```

unless the failed item is explicitly non-mandatory for that target release.

---

# 4. Reboot interruption failures

## UF-20 — Reboot occurs while update transaction expects resume

This is normal if update flow explicitly planned reboot.

After Windows startup:

1. Load durable `UpdateTransactionRecord`.
2. Determine expected resume phase.
3. Read actual installed/update evidence.
4. Continue verification or recovery.

Do not restart the whole update blindly from step zero.

---

## UF-21 — Unexpected power loss during update

### After reboot

```text
persisted transaction
+
current actual state
+
last known baseline
→ reconcile
```

Possible outcomes:

- continue supported update resume;
- verify old baseline still coherent;
- start Recovery.

Never infer target release from the mere fact that reboot succeeded.

---

## UF-22 — Transaction record missing/corrupt after reboot

If actual system state cannot be confidently mapped to a known supported baseline:

```text
RECOVERY_REQUIRED
```

If installed baseline identity and actual evidence prove previous known-safe state, normal operation may resume with update marked failed/aborted.

---

# 5. Post-apply verification failures

## UF-30 — Installer reports success, version evidence mismatches target

Example:

```text
Expected SplitOS release = 1.4
Observed baseline/runtime = mixed 1.3/1.4
```

Target commit prohibited.

Response:

```text
Recovery / rollback
```

---

## UF-31 — Target version present but mandatory runtime health checks fail

Example:

```text
files/version updated
but Privileged Broker cannot start
```

Version presence alone is insufficient.

If mandatory health criteria fail:

```text
new baseline not committed as healthy supported state
→ recovery
```

---

## UF-32 — Update succeeds technically but compatibility evidence fails after reboot

If validation criteria were explicitly mandatory for target release, commit is prohibited.

If issue is discovered only later after a previously valid commit, this becomes a post-commit regression, not an incomplete update transaction.

That distinction matters:

```text
pre-commit verification failure
!=
post-commit operational regression
```

---

# 6. Recovery scenarios

## RC-01 — Recovery restores previous baseline successfully

Required sequence:

```text
choose recovery target
→ apply recovery operations
→ read actual state
→ verify target
→ commit recovery result
```

Only then is recovery `COMPLETED`.

---

## RC-02 — Recovery operation commands succeed but target not verified

Classification:

```text
RECOVERY VERIFICATION FAILURE
```

Response:

- do not mark Recovery completed;
- attempt next allowed recovery strategy;
- if no automatic strategy can prove coherent state, enter manual recovery/support-required state.

---

## RC-03 — Recovery cannot restore SplitOS managed runtime, but Windows desktop is usable

Preferred safe result:

```text
Base Windows usable
Managed runtime disabled/degraded
Recovery attention required
```

This is preferable to risking bootability merely to restore premium functionality.

---

## RC-04 — Recovery requires disabling PRO runtime temporarily

Allowed when required for system safety.

```text
PRO entitlement may still exist
but ManagedRuntimeAccess can be temporarily unavailable due recovery state
```

Entitlement and runtime health are different facts.

---

## RC-05 — Recovery also fails

Escalation:

```text
S5 Manual Recovery / Support Required
```

Priority:

```text
User data integrity
→ bootable Windows
→ accessible desktop/recovery UI
→ diagnostics/support bundle
→ managed runtime restoration later
```

---

# 7. Update/Recovery canonical data rules

## InstalledBaselineIdentity

Must only change when target baseline is semantically verified and committed.

## UpdateTransactionRecord

Tracks attempt/resume/outcome; it does not redefine baseline identity by itself.

## RecoveryContext

Tracks recovery target, evidence and progress; it does not become authoritative proof that recovery succeeded.

---

# 8. Rollback target selection

Recovery target should prefer a known supported state rather than simply “whatever existed immediately before the error”.

Conceptual inputs:

```text
last known-good baseline
previous committed baseline
current actual evidence
available recovery artifacts
user data safety
bootability
compatibility knowledge
```

Exact snapshot/package implementation remains OPEN.

---

# 9. Update failure user experience

User-facing outcome should distinguish:

```text
Update postponed
Update failed before changes
Update failed and was rolled back
Update failed; recovery completed
Update failed; SplitOS runtime temporarily unavailable
Manual recovery required
```

Do not collapse all cases into `Update failed`.

---

# 10. Verification candidates

Later Specification/QA should derive tests including:

- power loss before apply;
- power loss during apply;
- reboot after staging;
- missing transaction record;
- partial package application;
- target version mismatch;
- broker unable to start after update;
- rollback command failure;
- recovery verification failure;
- Windows usable but SplitOS runtime unavailable.
