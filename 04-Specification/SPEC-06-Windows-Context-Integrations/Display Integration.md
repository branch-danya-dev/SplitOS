# SPEC-06 — Display Integration

## 1. Purpose

Defines how SplitOS discovers, identifies, applies and verifies Windows display topology/mode state for Work/Game policy.

The Display integration owns Windows display mechanism execution and evidence only.

It does not own:

- whether a profile should prefer Desktop or TV;
- whether 4K/120 is better than 1440p/280;
- Game Profile precedence;
- committed operational mode.

Those decisions remain with Game Profile/Mode owners.

---

## 2. Public API baseline

v1 baseline:

```text
GetDisplayConfigBufferSizes
QueryDisplayConfig
DisplayConfigGetDeviceInfo
SetDisplayConfig
```

`QueryDisplayConfig` is the canonical current-topology read path.

`SetDisplayConfig` is the canonical topology/mode mutation path.

Legacy `ChangeDisplaySettingsEx` is not the primary SplitOS multi-display orchestration mechanism.

---

## 3. Active display snapshot

The adapter builds `DisplaySnapshot` from active Windows display paths.

Conceptual shape:

```text
DisplaySnapshot
├── generation
├── observedUtc
├── topologyId where available
├── paths[]
│   ├── adapterLuid
│   ├── sourceId
│   ├── targetId
│   ├── active
│   ├── targetAvailable
│   ├── outputTechnology
│   ├── rotation
│   ├── scaling
│   ├── refreshRate rational
│   ├── source mode / desktop position
│   ├── target signal mode
│   └── identity metadata
└── capability flags
```

The query flow MUST handle `ERROR_INSUFFICIENT_BUFFER` by resizing and retrying because display state may change between sizing and query calls.

---

## 4. Current-path identity

For one snapshot, the Windows CCD target key is:

```text
adapterLuid + targetId
```

This is sufficient to address `DisplayConfigGetDeviceInfo` and current SetDisplayConfig operations.

It is **not** by itself the persistent user-profile display identity.

---

## 5. Persistent display selector material

For profile association across Runtime restarts/reboots, SplitOS stores a selector rather than blindly storing the current `targetId`.

Selector evidence may include:

```text
monitorDevicePath
PnP device instance ID when resolved through SetupAPI
EDID manufacture ID
EDID product code ID
connectorInstance
outputTechnology
friendly monitor name as weak display text only
last-known adapter identity hint
```

`DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorDevicePath` is the preferred bridge into SetupAPI for installed monitor identity.

Friendly name MUST NOT be used as a unique key.

EDID manufacturer/product alone MUST NOT be assumed unique because two identical monitors may exist.

---

## 6. Display selector resolution

A stored selector resolves against a fresh snapshot into:

```text
EXACT
UNIQUE_FALLBACK
AMBIGUOUS
NOT_FOUND
UNAVAILABLE
```

Resolution priority is release-validated but must follow this intent:

1. exact persisted monitor/device identity match;
2. unique installed-device/EDID + connector relationship match;
3. unique weaker fallback only if policy explicitly allows it;
4. otherwise `AMBIGUOUS`/`NOT_FOUND`.

SplitOS MUST NOT silently select the first monitor with the same friendly name.

For a `MANDATORY` target, ambiguity blocks target verification until the user resolves/rebinds or an approved fallback is selected.

---

## 7. Desired display target contract

The Display adapter receives an already-resolved semantic target such as:

```text
DisplayTarget
├── selectedDisplaySelector
├── topologyIntent
│   ├── SELECTED_ONLY
│   ├── EXTEND
│   └── PRESERVE_ACTIVE_TOPOLOGY
├── resolution
├── refreshRequirement
├── rotation
├── primary/desktop-position intent where supported
├── HDR/advancedColor intent where supported
└── fallback IDs already approved by policy resolution
```

SPEC-08 decides how Game Profiles generate this target.

---

## 8. Resolution and refresh representation

Resolution uses physical pixel dimensions from the Windows display configuration model.

Refresh MUST remain rational in adapter evidence:

```text
numerator / denominator
```

The adapter MUST NOT round 59.94 Hz to an integer 60 and then claim exact equality.

Higher layers may define acceptable equivalence/range semantics in the profile requirement, for example:

```text
EXACT_RATIONAL
AT_LEAST
CLOSE_TO_NOMINAL
MAX_SUPPORTED_UNDER_CONSTRAINTS
```

The adapter returns actual rational evidence for that comparison.

---

## 9. Apply sequence

Normal mode transition display changes use:

```text
fresh QueryDisplayConfig
→ resolve target paths/modes
→ SetDisplayConfig with SDC_VALIDATE + supplied config
→ if supported: SetDisplayConfig with SDC_APPLY + supplied config
→ fresh QueryDisplayConfig
→ verify
```

The exact valid flag combination MUST follow the current Windows API contract.

---

## 10. Windows persistence policy

Normal Work/Game mode transitions MUST NOT persist their temporary mode topology into the Windows display database merely as a side effect.

Therefore normal mode operations do **not** use:

```text
SDC_SAVE_TO_DATABASE
```

unless a future explicit product feature intentionally changes the user's durable Windows display configuration.

Reason:

```text
mode switch
!= permanent rewrite of user Windows display preference
```

Rollback/restoration uses a fresh re-resolution of the prior SplitOS source display target/snapshot evidence.

`SDC_USE_DATABASE_CURRENT` may be an explicit BASE/recovery fallback only when the policy intentionally means "return to Windows persisted topology" and compatibility testing approves it.

---

## 11. Verification

Display apply is successful only when fresh read-back proves all mandatory conditions.

Verification may include:

```text
expected target present and available
expected active/inactive path set
expected resolution
expected refresh requirement
expected rotation
expected topology relationship
expected primary/source-position requirement if used
expected HDR/advanced-color state if that capability is mandatory and supported
```

A successful `SetDisplayConfig` return without read-back is `APPLIED_NOT_VERIFIED`, not `APPLIED_VERIFIED`.

---

## 12. Hot-plug and race handling

Display topology may change while SplitOS is applying it.

If relevant display generation changes between target resolution and apply/verification:

```text
→ invalidate resolved target
→ refresh snapshot
→ re-resolve allowed fallback
or
→ STALE_SNAPSHOT / TARGET_UNAVAILABLE
```

A path may transiently remain active while `targetAvailable == FALSE`; SplitOS treats availability as explicit evidence and must not assume active path means a physical display is still reachable.

---

## 13. HDR / advanced color

Windows DisplayConfig exposes advanced color/HDR device-info capability families.

SplitOS v1 classifies them as:

```text
READ_CAPABILITY
→ SUPPORTED_PUBLIC_VERSION_GATED

WRITE_CAPABILITY
→ SUPPORTED_PUBLIC_VERSION_GATED
→ must be release/build validated before a profile may make HDR mandatory
```

Because Windows advanced-color contracts have evolved across Windows 11 builds, a release MUST probe/use the structure/type supported for its validated build set.

If HDR mutation is not validated on the current release:

```text
HDR target
→ PREFERRED/OPTIONAL only
or
→ USER_ACTION_REQUIRED if product flow explicitly asks the user
```

No undocumented registry toggle is accepted as the canonical fallback.

---

## 14. Dynamic refresh / virtual refresh

Windows 11 exposes display path metadata for virtual/dynamic refresh behavior.

SplitOS may observe these flags and preserve them when constructing supplied display configuration.

SPEC-06 does not yet make Dynamic Refresh Rate a separate user-facing Game Profile control.

A release MUST avoid accidentally clearing/altering virtual-refresh semantics when applying a mode if it did not intentionally resolve such a change.

---

## 15. Monitor configuration / DDC-CI

DDC/CI monitor controls such as physical brightness/contrast are outside the mandatory v1 mode transition baseline.

They may be added later behind a separate capability because:

- support differs by monitor/connection/driver;
- some TVs do not expose useful DDC/CI behavior;
- they are not required to prove resolution/refresh topology.

---

## 16. Failure mapping

| Condition | Result |
|---|---|
| selector exact match absent | `TARGET_NOT_FOUND` |
| multiple equivalent targets | `TARGET_AMBIGUOUS` |
| target disconnected | `TARGET_UNAVAILABLE` |
| SDC validation rejects target | `OPERATION_REJECTED` |
| SetDisplayConfig fails | `TECHNICAL_FAILURE` |
| SetDisplayConfig succeeds but read-back differs | `VERIFICATION_FAILED` |
| topology changed during operation | `STALE_SNAPSHOT` |
| HDR target requested but unsupported build/capability | `UNSUPPORTED_CAPABILITY` |

---

## 17. Example

Game profile requests:

```text
TV selector
3840x2160
120 Hz preferred
60 Hz approved fallback
SELECTED_ONLY
```

Flow:

```text
fresh snapshot
→ TV selector resolves exact
→ 4K120 validation succeeds?
   ├── yes → apply 4K120 → read back → verify
   └── no  → approved 4K60 fallback → validate/apply/read back/verify
```

Only the resolved immutable fallback used for this operation is journaled by SPEC-05.

---

## 18. Acceptance criteria

- current topology comes from CCD read APIs;
- persistent profile identity is not raw `targetId`;
- duplicate monitor ambiguity is explicit;
- mode display changes are temporary by default and do not overwrite Windows persisted topology;
- supplied topology is validated before apply;
- every mandatory display mutation is independently read back;
- hot-plug invalidates stale targets;
- refresh precision is preserved;
- HDR write behavior is version/capability gated;
- no registry/display-settings screen automation is required for normal supported display switching.
