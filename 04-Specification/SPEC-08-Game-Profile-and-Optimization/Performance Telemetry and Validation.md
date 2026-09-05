# SPEC-08 — Performance Telemetry and Validation

## 1. Purpose

Defines the runtime evidence used to determine whether a game/profile configuration actually satisfies its performance target.

Telemetry is evidence only.

```text
telemetry
!= user intent
!= GameProfile
!= canonical optimization recommendation
```

---

## 2. v1 telemetry strategy

SplitOS defines a provider abstraction:

```text
PerformanceTelemetryAdapter
```

Primary v1 implementation candidate:

```text
PresentMon 2.x compatible telemetry
```

because current PresentMon provides multi-vendor Windows frame/present metrics and an application-facing API/service model.

The exact packaging choice remains an engineering gate:

```text
PresentMon Service + SDK
vs
embedded/open-source analysis integration
```

This packaging choice MUST be validated before release and must not silently add an uncontrolled privileged/service dependency.

---

## 3. Required semantic metrics

Where available, a valid observation SHOULD provide:

```text
process/application identity
sample start/end
frame count
presented FPS
displayed FPS
frame-time distribution
CPU busy/wait
GPU busy/wait
dropped/displayed-frame evidence
present mode / tearing evidence where useful
display latency where available
```

Hardware telemetry such as power/temperature MAY be captured, but is not required to prove the core visual-performance contract.

---

## 4. Observation identity

```text
PerformanceObservation
├── observationId
├── gameId
├── gameSessionId
├── profileId + revision
├── recommendationId?
├── process proof-set reference
├── hardwareFingerprint
├── display context
├── config digest
├── telemetry provider/version
├── sample window
├── validity
├── aggregate metrics
└── raw-sample reference/retention metadata
```

An observation is immutable after finalization.

---

## 5. Process binding

Telemetry MUST bind to the SPEC-07 correlated game process/application set.

Forbidden:

```text
new foreground process
→ collect FPS
→ assume it is the game
```

If correlated primary game process is replaced according to a known bootstrap rule, telemetry may rebind only through the same session-proof model.

---

## 6. Sample windows

A sample has explicit lifecycle:

```text
PENDING
WARMUP
COLLECTING
FINALIZING
VALID
INVALID
```

Warmup avoids treating process startup/shader initialization as representative by default.

Exact warmup/sample durations are release/game policy and require SPEC-14 validation.

---

## 7. Sample validity

Validity values:

```text
VALID_REPRESENTATIVE
VALID_LOW_CONFIDENCE
INVALID_TOO_SHORT
INVALID_IDLE
INVALID_MENU_LIKELY
INVALID_PROCESS_MISMATCH
INVALID_CONTEXT_CHANGED
INVALID_GAME_CONFIG_CHANGED
INVALID_TELEMETRY_FAILURE
INVALID_SESSION_ENDED
```

The runtime SHOULD use game-specific knowledge to distinguish representative gameplay where possible.

If it cannot reliably distinguish gameplay from menu/idle, the sample remains lower confidence and cannot be treated as strong automatic degradation evidence.

---

## 8. Context immutability during sample

A measurement is invalidated or segmented when material context changes:

```text
display mode changes
profile revision changes
managed graphics config changes
GPU selection/driver context changes
game process proof set changes unexpectedly
mode leaves GAME
```

Controller reconnect normally does not invalidate graphics performance sampling unless it changes game/render behavior in a known material way.

---

## 9. Aggregate model

Do not optimize from average FPS alone.

A finalized observation SHOULD compute versioned aggregates such as:

```text
median FPS
low-percentile FPS / tail FPS
median frame time
p95/p99 frame time
dropped-frame ratio
frame-time variability metric
GPU busy percentile
CPU busy percentile
sample duration/frame count
```

Exact statistic names/provider mappings remain implementation-specific, but policy must use stable semantic meanings.

---

## 10. Target satisfaction

The optimization policy evaluates:

```text
PerformanceObservation
against
TargetPerformanceContract
```

Result:

```text
TARGET_SATISFIED
TARGET_SATISFIED_LOW_MARGIN
TARGET_UNSTABLE
TARGET_BELOW_FLOOR
TARGET_UNKNOWN
```

The thresholds are versioned policy data, not code magic.

---

## 11. Stability principle

A game that averages 120 FPS but repeatedly exhibits severe frame-time tails may be classified as not stably satisfying a 120 target.

Likewise a capped 60 FPS title with consistent pacing can satisfy a 60 target even though display supports 120/144/280 Hz.

This preserves:

```text
stable useful performance
>
headline average FPS
```

---

## 12. VSync / VRR / frame caps

Telemetry interpretation must consume known display/game context where available:

```text
refresh rate
VRR capability/state evidence
VSync intent
frame limit
frame-generation state
```

A frame cap can intentionally produce FPS below physical refresh and must not be classified as a failure when it matches the profile target.

---

## 13. Frame generation

Where provider/game knowledge can distinguish generated/displayed frames from native render cadence, the observation SHOULD retain both concepts.

Optimization MUST NOT represent generated displayed FPS as equivalent to native simulation/render performance for all latency decisions.

If provider cannot distinguish reliably:

```text
confidence downgraded
```

rather than inventing native FPS.

---

## 14. Bottleneck evidence

Optional derived classification:

```text
GPU_BOUND_LIKELY
CPU_BOUND_LIKELY
MIXED
DISPLAY_OR_CAP_LIMITED
UNKNOWN
```

This uses measured CPU/GPU frame activity plus game/display context.

It is advisory to Optimization Recommendation Engine.

It does not grant permission for driver/clock tuning.

---

## 15. Telemetry activation

Normal supported behavior:

```text
GAME_RUNNING_CONFIRMED
→ telemetry may attach to correlated game process
```

Telemetry MUST NOT delay Game Session confirmation solely because performance provider is unavailable unless a deliberate calibration workflow requires it.

Telemetry failure normally means:

```text
optimization refinement unavailable
```

not:

```text
game cannot run
```

---

## 16. Resource overhead

Telemetry implementation must have an overhead budget verified by SPEC-14.

It MUST support disabling/degrading telemetry when:

```text
provider causes incompatibility
anti-cheat compatibility concern exists
measurement overhead exceeds support threshold
user disables performance measurement where product policy allows
```

SplitOS does not inject into protected game processes to obtain telemetry.

---

## 17. Anti-cheat boundary

Telemetry must remain outside protected game internals.

Forbidden:

```text
DLL injection
memory hooks
protected memory reads
anti-cheat bypass
render API interception inside game process
```

Present/event tracing or other external supported mechanisms are preferred.

---

## 18. Raw data retention

Raw per-frame data can be large.

Default product model SHOULD retain:

```text
bounded recent raw samples for diagnostics/calibration
longer-lived aggregate observations
```

Exact retention/privacy policy belongs SPEC-13.

User profile/recommendation correctness must not depend on indefinite raw telemetry retention.

---

## 19. Privacy

Performance telemetry is local runtime evidence by default.

Uploading diagnostic/performance data to SplitOS backend is outside SPEC-08 and requires explicit SPEC-13 product/privacy policy.

No gameplay screen contents, keystroke contents or voice/audio content are required for the optimization contract.

---

## 20. Provider compatibility

A telemetry provider has compatibility state:

```text
SUPPORTED
VERSION_GATED
DEGRADED_METRICS
UNAVAILABLE
DISABLED
```

Optimizer consumes only metrics actually available.

Missing GPU busy must not become zero GPU busy.

Unavailable values remain unavailable.

---

## 21. PresentMon research baseline

Current public PresentMon project exposes metrics including frame time, displayed/presented FPS, CPU/GPU busy/wait, dropped frames and display latency, and current project code is MIT-licensed.

Before v1 release SplitOS MUST validate:

```text
redistribution notices
selected integration mode
service/process topology impact
API version compatibility
anti-cheat/game compatibility matrix
measurement overhead
```

---

## 22. Acceptance criteria

- telemetry binds only to correlated game process/session;
- menu/idle/too-short sample cannot silently become strong benchmark proof;
- context change invalidates/segments sample;
- average FPS alone is not target satisfaction;
- frame caps/display refresh are interpreted in target context;
- telemetry unavailable does not block normal launch by default;
- no injection/protected memory technique is required;
- raw telemetry retention is bounded and separable from canonical profiles;
- provider missing metric remains unknown, not zero.
