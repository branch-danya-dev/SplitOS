# SplitOS — Specification Traceability

## 1. Purpose

This document continues the Specification-level SSAD traceability chain.

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
| Broker-mediated protected `machine.db` persistence | Data Placement + Trust boundary + SPEC-03 |
| no SQL/path tunneling for machine persistence | Trust least-privilege invariant + SPEC-03 repository model |

---

## 4. SPEC-03 traceability

| SPEC-03 decision | A&D source |
|---|---|
| physical machine/user/cache separation | `11-Synthesis/Data and State Placement.md` placement classes |
| SQLite selected for structured local persistence | Synthesis storage technology OPEN closed in SPEC-03 |
| `machine.db` under ProgramData | MACHINE_CANONICAL / TRANSACTION_DURABLE placement |
| `user.db` under LocalAppData | USER_CANONICAL placement |
| `projection.db` separate and rebuildable | PROJECTION_CACHE placement |
| canonical DBs use WAL + `synchronous=FULL` | failure/reboot durability requirements |
| projection cache uses WAL + `synchronous=NORMAL` | rebuildable evidence semantics |
| machine DB writes mediated by Broker | Trust local privilege boundary + SPEC-02 |
| Runtime Host is user/cache DB writer | Synthesis Runtime Host + semantic ownership |
| Manager/Launcher never open DB directly | UI != canonical writer / interface boundary |
| OperationalModeState is durably machine-scoped | State/Failure/Synthesis crash-reconciliation requirement |
| ModeTransition/Update/Recovery records are transaction durable | States + Flows + Failures |
| GameProfile persists per user | Game Profiles ownership + USER_CANONICAL |
| external projections preserve source/freshness | Data/External Evidence Trust |
| auth secrets excluded from ordinary SQLite plaintext | Trust / Protected Secret placement |
| typed repository gateways instead of raw SQL | Ownership + Trust + Synthesis component rules |
| optimistic row revision is first-class | concurrent activation/crash-safe persistence closure |
| live DB backup uses SQLite-consistent backup | WAL physical semantics + recovery requirements |
| schema migration is explicit/versioned | release compatibility + recovery requirements |
| machine migration uses release-owned ID, not IPC SQL | Trust + Broker bounded capability model |
| projection corruption may rebuild; canonical corruption enters controlled recovery | Failure Model / safe-convergence priority |

---

## 5. Requirements primarily served

SPEC-01/02/03 provide infrastructure needed by:

```text
FR-ACCESS
FR-MODE / FR-TRANS
FR-APP
FR-GAME / FR-LAUNCHER
FR-UPDATE / FR-RECOVERY
FR-ACCOUNT / FR-ENT
NFR-REL
NFR-TRANS
NFR-SEC
NFR-OBS
```

They do not fully satisfy those requirements alone; later specs define domain algorithms, auth contracts and integrations.

---

## 6. Specification-level decisions

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

SPEC-DEC-008
v1 structured local persistence uses SQLite split into machine canonical, user canonical and rebuildable projection databases.

SPEC-DEC-009
machine/user canonical SQLite databases use WAL + synchronous=FULL; projection cache uses WAL + synchronous=NORMAL.

SPEC-DEC-010
machine-canonical SQLite is protected under ProgramData and normal Runtime Host writes cross bounded Broker persistence capabilities; ordinary user processes do not receive direct write ACL.

SPEC-DEC-011
per-user canonical data is stored under LocalAppData and written through Runtime Host persistence gateways; Manager/Game Launcher do not access databases directly.

SPEC-DEC-012
reusable account credentials are outside ordinary SQLite fields; DB stores only protected-secret references/metadata.

SPEC-DEC-013
physical schema evolution is monotonic/versioned; unsupported newer schema fails closed for writes and machine migrations are selected by trusted release migration ID.

SPEC-DEC-014
projection DB is disposable/rebuildable, while user/machine canonical corruption requires backup/recovery handling and must never fabricate canonical state.
```

If implementation validation disproves any of these, the decision must be revised explicitly rather than silently changed in code.

---

## 7. Verification backlog

### Runtime / IPC

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

### Persistence

```text
V-DATA-001 machine/user/projection DB physical separation
V-DATA-002 ordinary user process cannot directly write machine.db
V-DATA-003 Manager/Launcher cannot bypass Runtime Host persistence
V-DATA-004 canonical DB pragma/durability configuration verified
V-DATA-005 committed mode survives Runtime Host restart/reboot
V-DATA-006 incomplete transition is discoverable after Runtime Host crash
V-DATA-007 incomplete update/recovery survives reboot
V-DATA-008 projection.db deletion/corruption rebuilds without profile loss
V-DATA-009 user.db backup/restore preserves profiles/preferences
V-DATA-010 live backup is consistent while WAL is active
V-DATA-011 revision conflict is detected and not overwritten
V-DATA-012 disk-full/IO failure blocks required durable commit
V-DATA-013 machine migration cannot accept raw SQL from Runtime Host
V-DATA-014 unsupported newer schema opens no write path
V-DATA-015 machine DB corruption enters safe recovery without inventing mode
V-DATA-016 user DB contains no reusable plaintext auth token
V-DATA-017 stale projection cannot satisfy fresh evidence requirement
```

---

## 8. Next specification target

After SPEC-03 review/merge:

```text
SPEC-04 Account / Auth / Entitlement
```

This will define backend APIs, native-app authorization flow, token lifecycle/protection, Windows-user association, FREE/PRO entitlement and bounded offline authorization evidence.
