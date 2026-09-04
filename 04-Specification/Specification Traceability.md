# SplitOS — Specification Traceability

## 1. Purpose

This document begins the Specification-level continuation of the SSAD traceability chain.

```text
Requirement
→ Responsibility
→ Owner
→ State / Behavior
→ Data
→ Interface
→ Integration
→ Flow
→ Failure
→ Trust
→ Synthesis component
→ Detailed Specification
→ Verification case
```

---

## 2. SPEC-01 traceability

| SPEC-01 decision | A&D source |
|---|---|
| separate Manager / Game Launcher / Runtime Host / Broker processes | `11-Synthesis/Deployment and Process Topology.md` |
| Runtime Host is composition root but not giant owner | `11-Synthesis/Logical Component Architecture.md`, `01-Responsibilities`, `02-Ownership` |
| UI is not canonical writer | `02-Ownership`, `06-Interfaces`, `10-Trust` |
| one Runtime Host per Windows session | Synthesis process topology + SPEC user/session cardinality closure |
| one Broker per machine | Synthesis deployment topology |
| active physical console owns v1 machine mutations | machine-wide mode invariant + Windows session boundary; closed in SPEC-01 |
| FREE Runtime Host may run while managed runtime disabled | Runtime Access State + First Run behavior |
| GAME Launcher exists independently from `GAME_RUNNING` | Game Session State + Game Launch Behavior |
| Host restart reconciles instead of inventing mode | Failure Model / Runtime Failure Scenarios |
| major mutation gate shared by Mode/Update/Recovery | Flows + Failures + Synthesis |
| correlation / operation / request IDs | Flows + Observability responsibility + Synthesis handoff |
| local component protocol compatibility | Trust + Synthesis release coherence |

---

## 3. SPEC-02 traceability

| SPEC-02 decision | A&D source |
|---|---|
| Named Pipes selected for v1 local IPC | `07-Integrations`, `10-Trust/Local Privilege and IPC Trust.md`; candidate closed in SPEC-02 |
| explicit pipe DACL | Trust Model / Local Privilege Trust |
| remote pipe clients rejected | local-only trust boundary |
| Broker validates OS-derived client session/PID | Local Privilege Trust |
| only Runtime Host may call Broker | Synthesis component architecture / Trust |
| Manager/GameLauncher cannot call Broker | Interface + Trust + Synthesis |
| Broker is LocalSystem dedicated service in v1 | privileged machine-mutation boundary; service identity OPEN closed in SPEC-02 |
| service SID + required privilege minimization | Trust hardening requirements |
| no arbitrary command/script/raw admin API | Trust security invariant |
| bounded capability catalog | Interfaces + Trust + Synthesis |
| technical result != semantic commit | Ownership + Interfaces + Flows + Failures |
| length-prefixed bounded JSON protocol | SPEC-02 implementation decision |
| major/minor protocol handshake | Synthesis local version compatibility need |
| mutation idempotency key | Failure/Update/Recovery durability concerns |
| per-session Broker endpoint | active session trust boundary + SPEC-01 session model |

---

## 4. Requirements primarily served

SPEC-01/SPEC-02 implement infrastructure needed by requirement families:

```text
FR-ACCESS
FR-MODE / FR-TRANS
FR-APP
FR-GAME / FR-LAUNCHER
FR-UPDATE / FR-RECOVERY
NFR-REL
NFR-TRANS
NFR-SEC
NFR-OBS
```

They do not fully satisfy those requirements alone; later specs define domain algorithms/data/integrations.

---

## 5. New Specification-level decisions

The following are implementation-level decisions introduced by Detailed Specification and do not redefine product ownership/state semantics:

```text
SPEC-DEC-001
v1 machine control is granted only to current physical-console Windows session.

SPEC-DEC-002
Runtime Host starts per interactive logon through Task Scheduler interactive-token baseline.

SPEC-DEC-003
v1 local IPC transport is Windows Named Pipes.

SPEC-DEC-004
Broker uses a per-session pipe and validates OS-derived client PID/session plus trusted Runtime Host image identity.

SPEC-DEC-005
v1 Broker service account baseline is LocalSystem with mandatory hardening and service SID.

SPEC-DEC-006
v1 IPC wire format is 4-byte length-prefixed UTF-8 JSON with 256 KiB max payload.

SPEC-DEC-007
privileged mutations require allowlisted capability IDs and idempotency identity; arbitrary admin commands are prohibited.
```

If implementation validation disproves any of these, the decision must be revised explicitly rather than silently changed in code.

---

## 6. Verification backlog created by SPEC-01/02

Minimum future `SPEC-14` cases:

```text
V-RUNTIME-001 single Runtime Host per session
V-RUNTIME-002 multi-user physical-console control ownership
V-RUNTIME-003 Host crash/restart reconciliation
V-RUNTIME-004 Launcher crash while GAME
V-RUNTIME-005 Broker unavailable degraded behavior

V-IPC-001 unauthorized same-user process cannot invoke Broker
V-IPC-002 secondary/RDP session mutation denied
V-IPC-003 protocol major mismatch denied
V-IPC-004 remote pipe connection rejected
V-IPC-005 malformed/oversized frame rejected
V-IPC-006 duplicate mutation idempotency behavior
V-IPC-007 idempotency conflict rejected
V-IPC-008 arbitrary command/raw service/registry capability absent
V-IPC-009 Broker partial technical result does not cause semantic commit
V-IPC-010 Broker crash during mutation reconciles from evidence
```

---

## 7. Next specification target

After SPEC-01 and SPEC-02 review/merge:

```text
SPEC-03 Local Data & Persistence
```

This will choose actual local storage engines and schemas for the state that SPEC-01 modules currently own abstractly.
