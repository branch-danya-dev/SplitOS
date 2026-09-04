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

## 5. SPEC-04 traceability

| SPEC-04 decision | A&D source |
|---|---|
| Windows identity remains separate from SplitOS Account | Concept + Ownership + Identity Data |
| SplitOS account association begins after Windows sign-in | First Run Behavior / FL-01 |
| OAuth/OIDC Authorization Code native flow | Identity/Entitlement Trust; auth mechanism OPEN closed in SPEC-04 |
| external system browser | Trust native-app security baseline |
| PKCE S256 mandatory | Trust candidate / native public-client security |
| loopback `127.0.0.1` redirect | native-app integration mechanism closed in SPEC-04 |
| no desktop client secret | Trust public-client boundary |
| stable accountId/sub, not email, is canonical identity | Ownership/Data identity rules |
| user-scoped DPAPI protects reusable credentials | Trust candidate closed in SPEC-04 |
| refresh token rotation/replay handling | Trust secret lifecycle + failure semantics |
| entitlement remains backend canonical | Ownership + Data + Runtime Access |
| capability list is runtime authorization source | Responsibilities/Interfaces + future-proof tier separation |
| FREE disables managed runtime but leaves Windows usable | Runtime Access State / Failures |
| signed offline assertion authorizes bounded PRO offline | Identity/Entitlement Trust |
| offline proof bound to account + installation + association | Trust context binding |
| offline proof max 7 days + rollback detection | Trust/failure abuse-model closure |
| browser/checkout callback cannot grant PRO | External Evidence Trust / FL-01 |
| checkout payment evidence becomes entitlement only through backend | Payment boundary + Ownership |
| one active account association per Windows user | Data model + Synthesis placement |
| sign-out clears auth/offline proof but preserves profiles by default | identity separation from user configuration |

---

## 6. Requirements primarily served

SPEC-01..04 provide infrastructure/domain contracts needed by:

```text
FR-ACCOUNT
FR-FIRST
FR-ACCESS
FR-ENT
FR-MANAGER
FR-MODE / FR-TRANS
FR-APP
FR-GAME / FR-LAUNCHER
FR-UPDATE / FR-RECOVERY
NFR-REL
NFR-TRANS
NFR-SEC
NFR-OBS
```

Later specs still define mode algorithms, Windows integrations, game-client mechanisms and release/update security details.

---

## 7. Specification-level decisions

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

SPEC-DEC-015
v1 native authentication uses OAuth/OIDC Authorization Code in the external system browser.

SPEC-DEC-016
public native client requires PKCE S256 and contains no reusable client secret.

SPEC-DEC-017
v1 Windows native redirect uses `http://127.0.0.1:<ephemeral>/oauth/callback` with a one-transaction loopback listener.

SPEC-DEC-018
access-token nominal lifetime is 15 minutes and access token is memory-only where practical.

SPEC-DEC-019
refresh tokens rotate on every successful refresh; v1 server policy upper bounds are 30-day inactivity and 90-day absolute lifetime.

SPEC-DEC-020
reusable auth/offline material is protected with user-scoped DPAPI and hidden from Manager/Game Launcher.

SPEC-DEC-021
offline premium authorization uses signed JWS `OfflineEntitlementAssertion v1`, bound to account + installation + association, max 7-day validity and 5-minute clock-skew tolerance.

SPEC-DEC-022
premium runtime authorization checks explicit entitlement capabilities; tier string alone is insufficient.

SPEC-DEC-023
one Windows user profile has at most one active SplitOS Account association; switching account creates a new associationId.

SPEC-DEC-024
hosted checkout/browser completion cannot grant PRO directly; newer backend entitlement must be fetched and accepted.

SPEC-DEC-025
when premium authorization cannot be proven, managed runtime fails closed while base Windows remains usable.
```

If implementation validation disproves any of these, the decision must be revised explicitly rather than silently changed in code.

---

## 8. Verification backlog

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

### Authentication / entitlement

```text
V-AUTH-001 external browser used; embedded password UI absent
V-AUTH-002 PKCE S256 mandatory; plain rejected
V-AUTH-003 loopback listener binds only 127.0.0.1
V-AUTH-004 auth state mismatch rejected
V-AUTH-005 OIDC nonce mismatch rejected
V-AUTH-006 authorization code replay rejected
V-AUTH-007 auth transaction timeout destroys verifier/state
V-AUTH-008 no reusable desktop client secret exists
V-AUTH-009 refresh token absent from SQLite/logs/UI IPC
V-AUTH-010 DPAPI blob inaccessible to another normal Windows user
V-AUTH-011 refresh-token rotation/reuse behavior verified
V-AUTH-012 DPAPI failure causes REAUTH_REQUIRED
V-AUTH-013 sign-out removes protected auth/offline material

V-ENT-001 FREE cannot enable runtime.managed_modes
V-ENT-002 PRO requires explicit capability
V-ENT-003 newer backend entitlement supersedes stale local evidence
V-ENT-004 offline assertion bad signature rejected
V-ENT-005 assertion wrong account rejected
V-ENT-006 assertion wrong installation rejected
V-ENT-007 assertion wrong association rejected
V-ENT-008 assertion beyond max validity rejected
V-ENT-009 expired assertion disables premium offline
V-ENT-010 clock rollback suspicion blocks offline premium
V-ENT-011 base Windows remains usable when entitlement cannot be proven

V-ACC-001 one active association per Windows user
V-ACC-002 account switch creates new associationId
V-ACC-003 email change does not change canonical identity
V-ACC-004 sign-out preserves Game Profiles by default
V-PAY-001 checkout/browser return alone cannot enable PRO
V-PAY-002 backend checkout completion still requires entitlement refresh
V-PAY-003 duplicate checkout create is idempotent
```

---

## 9. Next specification target

After SPEC-04 review/merge:

```text
SPEC-05 Mode Runtime
```

This will define exact committed-mode persistence semantics, transition transaction schema, blocker evaluation, Work/Game policy representation, mutation coordination, rollback and entitlement-loss convergence.
