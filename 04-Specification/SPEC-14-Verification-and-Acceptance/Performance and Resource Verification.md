# SPEC-14 — Performance and Resource Verification

## 1. Purpose

Defines how SplitOS performance/resource non-functional requirements are converted into measurable release acceptance evidence.

Several NFRs intentionally still contain `TBD` values. This specification does not invent product numbers. Instead, it requires every production `ReleaseAcceptanceProfile` to bind the mandatory numeric thresholds before release qualification begins.

Core rule:

```text
undefined required performance threshold
→ gate BLOCKED
```

not:

```text
undefined
→ assume acceptable
```

---

# 2. Performance principles preserved

Existing product intent includes:

```text
low idle SplitOS overhead
no significant unnecessary GPU use outside visible UI
bounded RAM footprint
Launcher background should not consume significant CPU/GPU
Game Mode must not materially degrade game performance because of SplitOS overhead
stable performance > short peak FPS
maximize visual quality subject to stable target performance
```

Verification must measure these claims independently from optimizer recommendation correctness.

---

# 3. ReleaseAcceptanceProfile thresholds

Before GATE-09 can pass, the release profile must specify relevant values for at least:

```text
PERF_RUNTIME_IDLE_CPU_MAX
PERF_RUNTIME_IDLE_WS_MAX
PERF_BROKER_IDLE_CPU_MAX
PERF_BROKER_IDLE_WS_MAX
PERF_LAUNCHER_ACTIVE_CPU_MAX
PERF_LAUNCHER_ACTIVE_GPU_MAX
PERF_LAUNCHER_BACKGROUND_CPU_MAX
PERF_LAUNCHER_BACKGROUND_GPU_MAX
PERF_LAUNCHER_BACKGROUND_WS_MAX
PERF_MODE_W2G_DURATION_P50/P95/P99 or selected contract
PERF_MODE_G2W_DURATION_P50/P95/P99
PERF_GAME_LAUNCH_SPLITOS_OVERHEAD_MAX
PERF_GAME_FRAME_TIME_REGRESSION_MAX
PERF_GAME_FPS_REGRESSION_MAX where meaningful
PERF_DIAGNOSTICS_NORMAL_OVERHEAD_MAX
PERF_LOG_DISK_GROWTH_MAX
```

Not every product capability needs every threshold, but applicability must be explicit.

---

# 4. Measurement environment

Performance results require controlled environment metadata.

Record:

```text
Windows build/patch
power source and power scheme
CPU/GPU/RAM
GPU driver
storage device/class
active display resolution/refresh/HDR/VRR context
background workload policy
Game Client/game version
game profile/config
SplitOS candidate identity
diagnostic capture mode
```

A benchmark without environment identity is non-release evidence.

---

# 5. Warm-up and sample rules

Each metric defines:

```text
warm-up period
sample interval
sample duration
number of repetitions
outlier policy
statistic used
```

Do not compare one cold start against one warmed baseline.

For latency/distribution-sensitive operations, median alone is insufficient; use the percentile contract defined in the acceptance profile.

---

# 6. Baseline comparison model

Game overhead tests compare equivalent environments:

```text
A = supported Windows/SplitOS baseline with managed overhead disabled or defined clean comparison state
B = same machine/game/config with target SplitOS managed scenario
```

The comparison must control:

- game settings;
- resolution/refresh;
- driver;
- power state;
- background workloads;
- game/client build;
- run segment where practical.

Changing quality settings while claiming an overhead comparison invalidates the result unless the test explicitly measures optimizer outcome rather than product overhead.

---

# 7. Runtime idle resource tests

## PERF-001 — Runtime Host idle CPU

**Given** stable Windows session, no user action, no active diagnostic capture.

Measure after warm-up over profile-defined duration.

Pass if statistic <= `PERF_RUNTIME_IDLE_CPU_MAX`.

## PERF-002 — Runtime Host idle working set

Pass if <= threshold under defined stable scenario.

## PERF-003 — Broker idle overhead

Measure CPU/memory when no privileged operations are active.

## PERF-004 — Background I/O

Observe unnecessary periodic disk/network activity. If a numeric budget is declared, enforce it; otherwise classify unexpected sustained activity as investigation evidence.

---

# 8. Launcher resource tests

## PERF-010 — Launcher foreground

Measure active Home/Library/Game Details rendering on desktop and TV profile display classes.

## PERF-011 — Launcher background during game

After `GAME_RUNNING`:

```text
Launcher process remains resident
ordinary controller handling released
foreground released
```

Measure CPU/GPU/working set.

This is a key release gate because a resident hidden Launcher must not materially steal game resources.

## PERF-012 — Optional in-game panel

If panel is supported in release scope, measure its incremental resource cost separately.

---

# 9. Mode transition latency

Timing boundaries must be semantic.

For Work→Game:

```text
T0 = accepted user transition request
T1 = target GAME durable commit + Launcher active/ready outcome
```

Also capture sub-durations:

```text
blocker inspection
user-decision waiting     # excluded/included separately
policy resolution
Windows apply
verification
commit
Launcher readiness
```

User think time MUST NOT be mixed into system execution latency unless explicitly reported separately.

Equivalent boundaries apply Game→Work.

---

# 10. Game launch latency

Distinguish:

```text
SplitOS preparation overhead
external Game Client handoff latency
game startup latency
```

A slow game/client must not automatically be charged as SplitOS overhead.

Record timestamps for:

```text
Launch request accepted
profile resolved
config apply complete
client handoff submitted/accepted
first strong game evidence
GAME_RUNNING confirmed
```

---

# 11. Gaming regression verification

## 11.1 Metrics

Where supported by telemetry provider, evaluate at least:

```text
presented/displayed FPS where meaningful
frame-time median
frame-time p95/p99 or selected tail metric
1% low/equivalent robust low-performance metric
dropped frames where meaningful
GPU busy / CPU busy evidence when useful
```

Average FPS alone cannot prove stable performance.

---

## 11.2 Representative gameplay

Main menu/idling is not acceptable representative game performance evidence unless the test explicitly targets menus.

A qualifying scenario should use one of:

```text
reproducible built-in benchmark
scripted/replayable gameplay segment
controlled manual scenario with documented route
stable synthetic scene accepted for that game
```

Telemetry samples classified `INVALID_MENU_LIKELY`, `INVALID_TOO_SHORT`, `INVALID_CONTEXT_CHANGED`, etc. cannot satisfy acceptance.

---

## 11.3 Regression contract

For each qualified game cohort, the acceptance profile defines allowed SplitOS overhead/regression.

Example semantics only:

```text
frame-time tail regression <= configured threshold
AND
FPS regression <= configured threshold where applicable
```

SPEC-14 does not hard-code a universal percentage across all games/hardware.

---

# 12. Optimization acceptance

Optimization tests are different from overhead tests.

They verify:

```text
user locks respected
hard compatibility constraints respected
performance target evaluated correctly
quality ladder deterministic
TARGET_UNMET_WITH_USER_LOCKS emitted when appropriate
recommendation stable/reproducible for same input knowledge/context
```

A recommendation does not need to beat every vendor/game preset universally; it must satisfy the specified objective and explainable policy.

---

# 13. Diagnostics overhead

Normal local observability must be measured separately from deep diagnostic capture.

Profiles:

```text
NORMAL
ETW_CAPTURE
FULL_DEEP_DIAGNOSTICS
```

Production performance gate primarily uses NORMAL.

Deep diagnostics may have higher overhead but must remain bounded/known enough not to cause unsafe behavior.

---

# 14. Update/recovery performance

Where product threshold is defined, measure:

```text
update download excluded/included separately
staging duration
capsule creation/verification
apply duration
reboot-to-resume duration
post-update verification
recovery duration
```

Network throughput should not be confused with local updater efficiency.

Safety gates take priority over speed. A fast update that removes rollback safety fails regardless of timing.

---

# 15. Disk/storage budgets

Measure:

```text
installed SplitOS-owned footprint
staging peak
Recovery Capsule size
log retention growth
ETW/deep diagnostics peak
update free-space requirement
```

A release must declare minimum required free space for safe update/capsule operation where applicable.

Diagnostics eviction must follow SPEC-13 priority and never consume safety-reserved space in a way that invalidates Recovery Capsule requirements.

---

# 16. Performance test result model

Each performance result records:

```text
metricId
thresholdId/value
observed statistic
sample count/duration
candidateId
matrix cell
environment identity
baseline identity if comparison
raw artifact reference where retained
PASS/FAIL/BLOCKED
```

A chart/image alone is not sufficient machine-verifiable result evidence.

---

# 17. Variance handling

If measurement noise is material:

- increase repetitions;
- use declared percentile/confidence approach;
- identify thermal/power throttling;
- keep invalid runs but exclude only by predeclared rules;
- do not discard poor runs ad hoc because they are inconvenient.

---

# 18. Thermal/power-state control

Gaming and latency qualification should record or control:

```text
AC/battery state where relevant
thermal saturation/warm-up
Windows power policy
GPU power/clock conditions where observable
```

If hardware throttling invalidates a run, mark it invalid with evidence rather than silently replacing it.

---

# 19. Performance scope exclusion

A hardware/game cohort may be removed from production support if it cannot meet declared performance/compatibility goals.

The support claim must change before release; the failed mandatory test cannot simply be waived while retaining the support claim.

---

# 20. Result

GATE-09 passes only when all mandatory performance/resource thresholds are explicitly defined and demonstrated on the required support matrix with reproducible evidence.
