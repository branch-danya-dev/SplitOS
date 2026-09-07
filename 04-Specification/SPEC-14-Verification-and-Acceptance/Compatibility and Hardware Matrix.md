# SPEC-14 — Compatibility and Hardware Matrix

## 1. Purpose

Defines how SplitOS converts compatibility claims into explicit release verification cells.

Core rule:

```text
one successful developer machine
!= production compatibility claim
```

A production claim exists only when the corresponding matrix cell is declared supported and has required evidence.

---

# 2. Compatibility ownership

SplitOS owns compatibility decisions such as:

```text
Windows Base X + SplitOS Release Y → SUPPORTED
Game Client Version A + Adapter B → SUPPORTED
GPU/driver cohort C + display scenario D → SUPPORTED
```

External vendor release status alone does not make a combination supported.

---

# 3. Matrix dimensions

The release acceptance profile declares relevant dimensions.

## 3.1 Windows

```text
edition
architecture
build/feature version
required patch cohort
language where relevant
secure boot / firmware mode where material
```

## 3.2 CPU / platform

```text
x64 platform cohort
CPU vendor/family where behavior differs
virtualization/security features where material
```

## 3.3 GPU / driver

```text
GPU vendor
GPU architecture/family cohort
VRAM class where optimizer constraints depend on it
driver branch/version cohort
```

## 3.4 Displays

```text
single monitor
multi-monitor
desktop high refresh
TV 4K60
TV 4K120 where supported
HDR on/off where supported
VRR capability where supported
display reconnect/hot-plug scenarios
```

## 3.5 Audio

```text
standard default endpoint observation
headset/speakers/HDMI audio
audio stable-ID capable Windows version
user-mediated default switching scenario
```

Automatic system-wide default switching remains outside production support unless its owning SPEC is updated with a supported mechanism and acceptance coverage.

## 3.6 Input

```text
keyboard/mouse
Xbox-compatible controller
supported GameInput controller classes
specific controller profile binding
hot-plug / reconnect
```

## 3.7 Storage / recovery

```text
system SSD class
required free-space bands
Recovery Store layout
update staging capacity
WinRE availability
```

## 3.8 Game Clients

```text
Steam client cohort
Epic Games Launcher cohort
Microsoft Gaming package/app-registration cohort
Battle.net experimental cohort if tested
```

Capability status is per client/capability, not one binary support flag.

## 3.9 Games / configuration adapters

```text
game identity
external client binding
game build/version cohort
GameConfigAdapter version
OptimizationKnowledge version
benchmark/telemetry scenario
```

## 3.10 Upgrade/recovery paths

```text
source SplitOS release
target release
Windows base transition if any
schema transition
RecoveryAuthorization edge
```

---

# 4. Matrix cell identity

A `MatrixCell` conceptually contains:

```text
matrixCellId
acceptanceProfileId
Windows identity
hardware cohort
driver cohort
display/input/audio scenario
client/game cohort where relevant
update/recovery edge where relevant
required capabilities
risk tier
```

Not every test runs across every cross-product combination. The release profile defines coverage strategy while preserving all material risk dimensions.

---

# 5. Coverage strategy

Use risk-based combinatorial coverage rather than impossible full Cartesian explosion.

Categories:

```text
CORE_CELL
REPRESENTATIVE_CELL
BOUNDARY_CELL
REGRESSION_CELL
EXPERIMENTAL_CELL
```

### CORE_CELL

Must execute broad end-to-end suite.

### REPRESENTATIVE_CELL

Represents equivalent hardware/client cohort after equivalence is justified.

### BOUNDARY_CELL

Tests risk edges such as:

```text
minimum supported Windows build
low/high refresh boundary
minimum supported VRAM class
specific update source boundary
```

### REGRESSION_CELL

Re-runs known historical failure conditions.

### EXPERIMENTAL_CELL

Does not create production support claim.

---

# 6. Equivalence classes

Hardware/client equivalence must be documented, not assumed.

Example:

```text
GPU models A/B/C
→ same architecture + driver path + tested behavior
→ may share one representative cohort for selected non-performance cases
```

Performance-sensitive or device-specific cases may still require individual evidence.

A new driver architecture/vendor cannot inherit equivalence from an unrelated cohort.

---

# 7. Windows compatibility gates

For each supported Windows Base:

Minimum acceptance includes:

```text
build/source verification
clean install/OOBE
Runtime/Broker start
account FREE/PRO
mode activation/switch
Windows context read/apply/read-back
supported Game Client path
update/recovery
WinRE/recovery path
security trust validation
observability/export
```

A newly released Microsoft Windows build is `UNKNOWN/UNSUPPORTED` until validated and approved.

---

# 8. Windows patch compatibility

Critical/security patch integration may use accelerated validation, but must still cover material risk areas.

At minimum for an approved Microsoft patch:

```text
boot/OOBE/runtime start where applicable
Broker/service/IPC
Windows context APIs
GameInput/display/audio discovery
Game Client integration smoke
mode transition
update/recovery safety
security/trust regression
```

Affected-area analysis determines expanded suites.

---

# 9. Component Matrix compatibility

Every accepted destructive or mode-managed Windows component decision must have evidence tied to a Windows Base/release context.

For `REMOVE`:

```text
mechanism verified
boot verified
OOBE verified
servicing verified
recovery verified
compatibility verified
```

If a Windows update changes dependency behavior, previous component decision evidence may become stale.

---

# 10. Display matrix

Minimum production scenarios should explicitly declare which of these are supported:

```text
single desktop monitor
single TV
mixed desktop + TV
two desktop displays
display unplug during transition
display unplug during game launch
same-model duplicate displays
refresh-rate mismatch/fallback
HDR capability presence/absence
```

Duplicate display identity tests must verify ambiguity handling; SplitOS cannot silently choose “first LG monitor” when stable selector cannot uniquely resolve.

---

# 11. Controller matrix

GameInput-based verification includes:

```text
initial enumeration
connect/disconnect callback
reconnect identity behavior
specific-device selector
allowed fallback policy
Launcher semantic navigation
hidden Launcher input isolation during gameplay
```

Exact in-game global panel chord remains outside release support until the owning engineering gate is closed.

---

# 12. Game Client capability matrix

For each client/version cohort, track independently:

```text
CLIENT_DISCOVERY
LIBRARY_DISCOVERY
INSTALLATION_EVIDENCE
GAME_LAUNCH
GAME_PROCESS_CORRELATION
GAME_EXIT_CORRELATION
AUTH_REQUIRED_HANDLING
REFRESH_INVALIDATION
```

Example release posture may be:

```text
Steam              TARGET_SUPPORTED_V1
Epic               TARGET_SUPPORTED_V1
Microsoft Gaming   PARTIAL_SUPPORTED_V1
Battle.net         EXPERIMENTAL
```

A local metadata parser failure may block library capability without blocking a separately supported public launch mechanism if the release contract allows that degraded composition.

---

# 13. Game matrix

A game is production-supported for optimization/config mutation only when its adapter/knowledge cohort has verified:

```text
identity mapping
install resolution
config read
legal values/dependencies
safe write location
source-digest conflict behavior
read-back verification
user-lock precedence
game-version compatibility
representative performance/telemetry path where claimed
```

Unsupported game may still be launchable through client integration if product scope permits, but SplitOS must not claim supported config optimization without adapter evidence.

---

# 14. Driver update behavior

Driver updates can invalidate hardware evidence/compatibility.

For release support:

- declare allowed driver cohort/range or policy;
- detect material driver change;
- re-evaluate hardware/profile context;
- do not assume old performance/VRR/display evidence remains valid forever.

A driver version outside known support may degrade capability or require compatibility refresh rather than silently remaining fully supported.

---

# 15. Upgrade matrix

Every supported source→target release edge must test:

```text
eligibility
artifact verification
capsule creation
schema migration
reboot/resume
health verification
commit
user data preservation
rollback compatibility
recovery edge authorization
```

Skipping intermediate versions is supported only if explicitly present in update matrix and migration/recovery contracts.

---

# 16. Recovery matrix

Recovery tests include:

```text
normal previous-release rollback
post-commit repair/recovery
WinRE recovery tool path
capsule unavailable/corrupt
user data created after update
schema compatibility/rollback bridge
Windows-level failure handoff to Windows recovery
```

---

# 17. Compatibility status model

Per matrix cell/capability:

```text
SUPPORTED
PARTIAL
EXPERIMENTAL
UNSUPPORTED
BLOCKED_PENDING_EVIDENCE
```

`PARTIAL` must enumerate exact supported capabilities; it cannot be vague marketing language.

---

# 18. Support-claim change

If a release candidate fails one cohort:

```text
fix
or
remove/restrict support claim
```

Changing support scope creates a new acceptance profile version and may require retesting selection/UX/compatibility surfaces that expose support status.

---

# 19. Evidence reuse

Evidence from an earlier release may be reused only when its material dependencies are proven unchanged and the acceptance policy explicitly allows inheritance.

At minimum record why evidence remains valid.

Examples that typically invalidate reuse:

```text
Windows build change
GPU driver branch change
Game Client update
component removal decision change
Broker/security boundary change
GameConfigAdapter change
update/recovery implementation change
```

---

# 20. Result

The compatibility matrix is the operational meaning of “SplitOS supports X”. A production support claim without a frozen matrix and required verification evidence is not a valid SplitOS release claim.
