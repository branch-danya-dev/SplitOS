# SplitOS — Detailed Specification

## Purpose

Этот каталог переводит завершённый Analysis & Design baseline в implementable specifications.

Specification не переопределяет уже зафиксированные product/ownership/state/trust semantics. Если implementation evidence конфликтует с A&D, изменение должно пройти обратно через Decision/Requirement/A&D/Synthesis chain.

## Status

| Package | Status | Scope |
|---|---|---|
| `SPEC-01` Runtime Process & Module | READY FOR REVIEW | physical processes, Runtime Host module boundaries, startup/lifecycle, session cardinality, version compatibility |
| `SPEC-02` Local IPC & Privileged Broker | READY FOR REVIEW | Named Pipe transport, protocol, caller validation, broker capabilities, service hardening; machine-state persistence extension from SPEC-03 |
| `SPEC-03` Local Data & Persistence | READY FOR REVIEW | SQLite stores, machine/user/cache separation, schemas, durability, migrations, corruption recovery |
| `SPEC-04` Account/Auth/Entitlement | NEXT | backend/auth/offline entitlement |
| `SPEC-05` Mode Runtime | NOT STARTED | persisted mode/transition schema, blocker/policy engine |
| `SPEC-06` Windows Context Integrations | NOT STARTED | display/audio/input/power/process/services/hardware |
| `SPEC-07` Game Client Adapters | NOT STARTED | Steam/Epic/Xbox/Battle.net adapters |
| `SPEC-08` Game Profile & Optimization | NOT STARTED | profile/optimization schema and resolution |
| `SPEC-09` Game Launcher & Shared Apps UX | NOT STARTED | controller-first UX and Shared Apps |
| `SPEC-10` Builder & Component Matrix | NOT STARTED | source/build manifest/component decisions |
| `SPEC-11` Update & Recovery | NOT STARTED | update transaction/reboot/rollback/recovery |
| `SPEC-12` Release Security & Key Management | NOT STARTED | signing/key hierarchy/revocation |
| `SPEC-13` Observability & Diagnostics | NOT STARTED | events/correlation/privacy/retention |
| `SPEC-14` Verification & Acceptance | NOT STARTED | executable acceptance/test cases |

## Specification rules

```text
A&D semantic owner
→ Specification contract
→ implementation
→ verification
```

Normative keywords:

- **MUST / MUST NOT** — required for conformance;
- **SHOULD / SHOULD NOT** — strong default; deviation requires documented evidence;
- **MAY** — optional compatible behavior;
- **OPEN** — unresolved and must not be silently guessed in implementation.

## Current physical baseline

```text
Windows machine
│
├── SplitOSBroker Windows Service                exactly 1 / machine
│
└── Windows interactive sessions
    └── per eligible session
        ├── SplitOS.RuntimeHost.exe              exactly 1
        ├── SplitOS.Manager.exe                  0..1
        └── SplitOS.GameLauncher.exe             0..1
```

Only the active physical console session may own machine-wide managed Runtime control in v1.

## Current IPC baseline

```text
Manager / Game Launcher
        ↓ user-session Named Pipe
Runtime Host
        ↓ authenticated + authorized per-session Named Pipe
Privileged Broker
        ↓ bounded privileged operations
Windows machine state
```

No UI process may call the Privileged Broker directly.

## Current persistence baseline

```text
%ProgramData%\SplitOS\Data\machine.db
→ SQLite WAL + synchronous=FULL
→ machine canonical + durable transactions
→ Broker-mediated write boundary

%LocalAppData%\SplitOS\Data\user.db
→ SQLite WAL + synchronous=FULL
→ per-user canonical profiles/preferences/association metadata
→ Runtime Host write boundary

%LocalAppData%\SplitOS\Cache\projection.db
→ SQLite WAL + synchronous=NORMAL
→ rebuildable external projections
→ Runtime Host cache boundary
```

Reusable account credentials are not stored as ordinary SQLite plaintext fields; protected-secret semantics continue in `SPEC-04`.

Canonical persistence rules:

```text
storage writer != semantic owner
persistence commit != external target verification
projection cache != authoritative external truth
machine DB != directly writable by ordinary user processes
```

## Current SPEC-03 artifacts

```text
SPEC-03-Local-Data-and-Persistence/
├── Local Data and Persistence Specification.md
├── Physical Storage Layout.md
├── SQLite Schema Baseline.md
├── Durability Migration and Corruption Model.md
├── Persistence Contract and Repository Model.md
└── persistence-topology.mmd
```

`SPEC-02` additionally contains the normative machine-state persistence capability extension required to protect `machine.db` behind Broker authority.

## Source architecture

Detailed Specification is based on:

```text
03-Analysis-and-Design/11-Synthesis/
```

and ultimately preserves the canonical models from Boundaries through Trust.
