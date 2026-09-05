# SPEC-14 — Traceability

## 1. Purpose

Maps the final verification package back to canonical requirements, A&D invariants and SPEC-01..13 contracts.

The traceability direction is:

```text
Requirement / decision
→ owning A&D/SPEC contract
→ SPEC-14 gate / case family
→ release evidence
```

SPEC-14 does not become a new owner of runtime semantics.

---

# 2. Top-level handoff

The Synthesis `Specification Handoff` defined SPEC-14 minimum families:

```text
Build verification
FREE runtime access
PRO entitlement activation
Work→Game
Game launch
Game exit
Game→Work
Device loss/fallback
Runtime Host/Broker crash
Offline entitlement
Update/reboot/resume
Recovery
Trust boundary abuse tests
Artifact tampering/signature tests
```

All are represented in the gate/case model.

---

# 3. Requirements → gates

| Requirement / concern | Primary gate(s) | Verification family |
|---|---|---|
| Distribution known baseline | GATE-00, GATE-01 | `VA-BUILD-*`, `VA-INSTALL-*` |
| FREE stable Windows experience | GATE-02 | `VA-IDENTITY-*`, `VA-ENT-*` |
| PRO entitlement correctness | GATE-02, GATE-08 | `VA-ENT-*`, `SEC-C-*` |
| Work/Game mutual exclusion | GATE-04, GATE-06 | `VA-MODE-*`, mode fault injection |
| Safe transition/blockers | GATE-04, GATE-06 | `VA-MODE-*`, `FI-*` |
| Windows actual-state verification | GATE-04 | `VA-WIN-*` |
| Game launch/session correctness | GATE-05, GATE-06 | `VA-GAME-*`, `FI-GAME-*` |
| Game profile/optimization | GATE-05, GATE-09 | `VA-PROFILE-*`, performance cases |
| Controller-first Launcher | GATE-05 | `VA-LAUNCHER-*` |
| Shared Apps bounded presentation | GATE-05 | `VA-SHARED-*` |
| Update safety | GATE-07, GATE-06 | `VA-UPDATE-*`, update fault matrix |
| Recovery / last-known-good | GATE-07, GATE-06, GATE-08 | `VA-RECOVERY-*`, `FI-REC-*`, `SEC-G-*` |
| User data preservation | GATE-07, GATE-11 | migration/rollback/data cases |
| Release supply-chain trust | GATE-08 | `SEC-E/F/G/I-*` |
| Least privilege / Broker trust | GATE-03, GATE-08 | `VA-IPC-*`, `SEC-A/B-*` |
| Observability | GATE-11 | `VA-OBS-*`, diagnostic acceptance |
| Privacy / secret redaction | GATE-11, GATE-08 | diagnostics/privacy + `SEC-H-*` |
| Performance/resource budgets | GATE-09 | performance profile cases |
| Compatibility | GATE-10 | matrix cell suites |
| Final production readiness | GATE-12 | readiness/sign-off |

---

# 4. NFR traceability

## Performance

Relevant requirement families:

```text
NFR-PERF-001..007
NFR-PERF-100..104
NFR-TRANS-100..101
```

Verification:

```text
Performance and Resource Verification.md
GATE-09
ReleaseAcceptanceProfile numeric thresholds
```

Existing `TBD` limits are intentionally not invented by SPEC-14; unresolved required limits block the gate.

---

## Reliability / transitions

Relevant families:

```text
NFR-REL-001..005
NFR-TRANS-001..009
NFR-REL-500..502
```

Verification:

```text
GATE-04
GATE-06
VA-MODE-*
Fault Injection and Recovery Verification.md
```

Canonical state model remains source of truth for terminal outcomes; older requirement wording such as `ROLLED_BACK` is interpreted through the canonical A&D state model where rollback is a mechanism and terminal result remains `CANCELLED` or `FAILED_WITH_SAFE_FALLBACK` as applicable.

---

## Update / recovery

Relevant families:

```text
NFR-UPD-001..006
NFR-UPD-100..102
NFR-UPD-200..203
NFR-REC-001..006
FR-UPDATE-*
FR-RECOVERY-*
```

Verification:

```text
GATE-07
GATE-06
Build Install Update Acceptance.md
FI update/recovery matrix
```

Includes later clarified product requirements for independent SplitOS update channel, mandatory previous-release capsule and user-data-preserving software rollback.

---

## Data protection / privacy

Relevant families:

```text
NFR-DATA-001..005
NFR-DATA-100..102
```

Verification:

```text
Data Privacy and Diagnostics Acceptance.md
GATE-07
GATE-11
```

Key invariants:

```text
software rollback != user-data rollback
local discovery != remote telemetry
```

---

## Security

Relevant families:

```text
NFR-SEC-001..006
NFR-UPD-003
```

plus Trust/SPEC-12 decisions.

Verification:

```text
Security and Trust Verification.md
GATE-03
GATE-08
```

---

## Observability

Relevant families:

```text
NFR-OBS-001..005
```

Verification:

```text
GATE-11
VA-OBS-*
Data Privacy and Diagnostics Acceptance.md
```

---

# 5. SPEC package traceability

## SPEC-01 Runtime Process & Module

Verified by:

```text
GATE-03
process cardinality
console-session ownership
Runtime restart/reconciliation fault tests
```

## SPEC-02 IPC & Broker

Verified by:

```text
VA-IPC-*
SEC-A-*
SEC-B-*
FI-BROKER-*
FI-LEASE-*
```

## SPEC-03 Local Data & Persistence

Verified by:

```text
VA-DATA-*
DATA-*
persistence/storage fault injection
migration/corruption fixtures
```

## SPEC-04 Account/Auth/Entitlement

Verified by:

```text
VA-IDENTITY-*
VA-ENT-*
SEC-C-*
FI-NET-*
clock faults
```

## SPEC-05 Mode Runtime

Verified by:

```text
VA-MODE-*
GATE-04
mode kill-point matrix
lease/fencing tests
```

## SPEC-06 Windows Context Integrations

Verified by:

```text
VA-WIN-*
FI-DEVICE-*
Compatibility and Hardware Matrix.md
```

## SPEC-07 Game Client Adapters

Verified by:

```text
VA-GAME-*
FI-GAME-*
Game Client capability matrix
external evidence security tests
```

## SPEC-08 Game Profile & Optimization

Verified by:

```text
VA-PROFILE-*
GATE-09 optimization/performance cases
hardware/profile matrix
```

## SPEC-09 Game Launcher & Shared Apps

Verified by:

```text
VA-LAUNCHER-*
VA-SHARED-*
Launcher performance tests
controller/display matrix
```

## SPEC-10 Builder & Component Matrix

Verified by:

```text
VA-BUILD-*
VA-INSTALL-*
GATE-01
Build Install Update Acceptance.md
Component Matrix compatibility evidence
```

## SPEC-11 Update & Recovery

Verified by:

```text
VA-UPDATE-*
VA-RECOVERY-*
FI update/recovery matrix
GATE-07
```

## SPEC-12 Release Security & Key Management

Verified by:

```text
SEC-E-*
SEC-F-*
SEC-G-*
SEC-I-*
GATE-08
```

## SPEC-13 Observability & Diagnostics

Verified by:

```text
VA-OBS-*
GATE-11
Data Privacy and Diagnostics Acceptance.md
```

---

# 6. Gate → evidence traceability

```text
GATE-00
→ candidate identity / BuildReceipt / release metadata evidence

GATE-01
→ build + install case results

GATE-02
→ identity/entitlement case results

GATE-03
→ runtime/IPC/persistence/security boundary results

GATE-04
→ mode/Windows integration results

GATE-05
→ game/profile/Launcher results

GATE-06
→ fault-injection campaign results

GATE-07
→ update/recovery/data-preservation edge results

GATE-08
→ security/trust campaign results

GATE-09
→ threshold-bound performance results

GATE-10
→ compatibility matrix coverage results

GATE-11
→ observability/privacy/support bundle results

GATE-12
→ ReleaseReadinessRecord + sign-offs
```

---

# 7. Open engineering research and verification

Earlier Synthesis research tracks map into verification as follows:

```text
ENG-01 Component removal matrix
→ GATE-01/GATE-10

ENG-02 Default audio switching
→ capability remains unsupported/user-mediated until owning spec changes; compatibility cases enforce no undocumented fallback

ENG-03 Game client adapters
→ GATE-05/GATE-10

ENG-04 Update/rollback technology
→ GATE-06/GATE-07

ENG-05 Windows source acquisition
→ GATE-00/GATE-01; automatic acquisition remains outside supported v1 unless separately validated

ENG-06 Broker hardening
→ GATE-03/GATE-08

ENG-07 Offline entitlement abuse
→ GATE-02/GATE-08

ENG-08 Performance baseline
→ GATE-09
```

---

# 8. Change propagation

If verification disproves a current specification:

```text
Verification evidence
→ update Discovery/Decision status
→ update affected Requirement
→ update owning A&D layer if semantic
→ update Synthesis
→ update owning SPEC
→ update SPEC-14 case/gate if needed
```

Do not “fix” contradiction by weakening the test while leaving source-of-truth documents unchanged.

---

# 9. Completion condition

SPEC-14 traceability is complete when every production-scope requirement/critical invariant can identify:

```text
owner/spec contract
verification family
release gate
required evidence
```

This package provides that final verification route for the current v1 baseline.
