# SPEC-08 — Overrides, Drift and Reconciliation

## 1. Purpose

Defines what SplitOS does when actual game configuration differs from the last recommendation/profile-managed state.

This is required because differences may come from:

```text
user changed a setting in-game
user edited config manually
game update migrated settings
game safe mode reset graphics
game launcher repaired configuration
mod changed config
SplitOS applied an older recommendation before profile edit
```

SplitOS MUST NOT assume every difference was intentional user preference, and MUST NOT blindly overwrite every difference either.

---

## 2. Core distinction

```text
Canonical User Override
!=
External Configuration Drift
```

A canonical override exists only when accepted/created through a defined user-intent action.

External drift is evidence.

---

## 3. Managed field state

For each managed setting, reconciliation may classify:

```text
IN_SYNC
EXTERNAL_CHANGE_DETECTED
LOCKED_USER_OVERRIDE
EXTERNAL_CHANGE_TEMPORARILY_PRESERVED
CONFLICT_UNSUPPORTED_LOCK
CONFLICT_SCHEMA_CHANGED
UNMANAGED
```

---

## 4. Last applied fingerprint

After successful `APPLIED_VERIFIED`, SplitOS stores derived metadata:

```text
profileId/profileRevision
recommendationId
configInstanceId
sourceDigest/resultDigest
managedSettingValues digest
appliedUtc
adapterVersion
```

This is reconciliation evidence, not canonical desired intent.

---

## 5. Detecting drift

Before automatic apply:

```text
read current managed config
↓
compare against last verified applied state
↓
compare against current canonical overrides
↓
classify differences
```

If no last-applied evidence exists, current game config is treated as initial external state rather than assumed drift.

---

## 6. External change default behavior

If a managed AUTO field changed externally after last verified apply:

```text
AUTO recommendation value = HIGH
actual external value = MEDIUM
```

v1 default:

```text
preserve current external value for the immediate launch
→ mark EXTERNAL_CHANGE_DETECTED
→ do not silently turn it into permanent UserOverride
→ expose reconciliation action in Manager/Launcher
```

This prevents SplitOS from fighting the user's in-game changes while also avoiding false assumptions about who/what changed the setting.

---

## 7. User reconciliation choices

For a supported changed field:

```text
KEEP_AS_OVERRIDE
RESTORE_SPLITOS_RECOMMENDATION
LEAVE_USER_MANAGED
RESET_TO_AUTO
```

### KEEP_AS_OVERRIDE

Creates/updates canonical `LOCKED` user override with the observed supported value.

### RESTORE_SPLITOS_RECOMMENDATION

Allows optimizer/adapter to reapply current valid recommendation.

### LEAVE_USER_MANAGED

Removes field from current managed-setting set where policy supports it.

### RESET_TO_AUTO

Clears explicit override and re-enters optimizer-controlled state.

---

## 8. No silent preference promotion

Forbidden:

```text
game safe-mode changed Ultra → Low
↓
SplitOS assumes user loves Low
↓
creates LOCKED override
```

External change becomes canonical user preference only through an explicit policy/user action that is auditable.

---

## 9. Existing locked field changed externally

If a field already has canonical lock:

```text
lock = RT HIGH
actual = RT OFF
```

classification:

```text
LOCKED_OVERRIDE_DRIFT
```

v1 default before next managed launch:

- show/record conflict;
- if adapter/current game version remains supported, AUTO mode may restore the locked value only after confirming config source has not changed again;
- if the game appears to have intentionally reset it due compatibility/safe mode, require reconciliation rather than endless write-reset loop.

---

## 10. Game version/schema change

When game/config schema version changes:

```text
all previous physical config mappings
→ require adapter compatibility revalidation
```

Canonical profile/overrides remain stored semantically.

Possible result:

```text
supported semantic override remains legal
→ remap through new adapter

setting removed/unsupported
→ SUSPENDED_CONFLICT
```

SplitOS MUST NOT delete the user's semantic intent simply because one game version removed the physical field.

---

## 11. Hardware change and overrides

Hardware changes invalidate recommendations, not automatically overrides.

Example:

```text
old GPU supports RT
new GPU/game combination does not
user lock RT=HIGH
```

Result:

```text
UserOverride preserved
state = SUSPENDED_CONFLICT
EffectiveConfiguration uses feasible value
UI explains conflict
```

If hardware later supports it again and current game knowledge validates it, user can reactivate/reset conflict.

---

## 12. Display change

A profile's 4K target remains desired intent even when current fallback is 1440p.

Temporary display fallback MUST NOT be persisted as a graphics setting override unless the user explicitly changes the profile.

Example:

```text
TV disconnected
→ launch Desktop fallback at 1440p
```

must not mutate:

```text
TV profile desired resolution = 4K
```

---

## 13. Recommendation change

New release knowledge may change recommendation:

```text
Recommendation R1: Shadows High
Recommendation R2: Shadows Medium
```

If field is AUTO:

```text
new recommendation may apply
```

If field is LOCKED:

```text
lock wins if valid
```

The recommendation version never silently rewrites locked user intent.

---

## 14. Profile revision race

If profile changes while recommendation/apply is in progress:

```text
expected profileRevision != current revision
→ STALE_PROFILE_REVISION
→ abort apply / re-resolve
```

A stale operation must not persist reconciliation decisions into a newer profile version.

---

## 15. Config source race

If game/launcher/user changes config between read and write:

```text
expectedSourceDigest != currentSourceDigest
→ SOURCE_CHANGED
```

Runtime re-reads and reconciles.

No blind overwrite retry.

---

## 16. Repeated drift loop

If the same managed field is repeatedly reset by the game after SplitOS applies it:

```text
repeated VERIFY_MISMATCH / drift
```

Compatibility state may downgrade:

```text
AUTO_WRITE
→ SUGGEST_ONLY
```

for that game/version/setting.

This is preferable to an endless tug-of-war.

---

## 17. Mods

When mods introduce/modify managed fields and adapter cannot safely distinguish ownership:

```text
AUTO write affected fields
→ disable/degrade
```

The user may retain profile/system optimization while game-setting configuration becomes `USER_MANAGED`.

SplitOS does not remove mods as reconciliation.

---

## 18. Reset operations

Manager SHOULD provide bounded actions:

```text
Reset setting to Auto
Reset all profile setting overrides
Recompute recommendation
Restore last verified SplitOS recommendation
```

`Restore last verified` is available only if adapter/game version compatibility still validates that assignment set.

---

## 19. Reconciliation record

Useful derived/audit record:

```text
ReconciliationEvent
├── eventId
├── profileId/revision
├── gameId
├── settingKey(s)
├── previous managed value
├── observed value
├── classification
├── resolution action?
├── source digest
└── timestamp
```

Retention belongs SPEC-13.

It is not canonical override truth by itself.

---

## 20. UX semantics

User-facing language SHOULD distinguish:

```text
"You locked this setting"
vs
"The game changed this setting"
vs
"This setting is no longer supported"
```

Avoid pretending SplitOS knows intent from a file difference alone.

---

## 21. Acceptance criteria

- external game setting change does not silently become persistent override;
- v1 preserves external change for immediate launch instead of blindly overwriting it;
- user can explicitly keep change as override or restore recommendation;
- safe-mode resets do not permanently poison profile;
- hardware/game-version conflict suspends unsupported lock without deleting it;
- stale profile revision/source digest prevents overwrite;
- repeated game self-reset can downgrade AUTO write capability;
- temporary display fallback does not rewrite canonical profile target.
