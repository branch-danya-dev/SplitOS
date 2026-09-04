# SPEC-07 — Traceability

## 1. Source mapping

| SPEC-07 decision | Primary source |
|---|---|
| one stable semantic adapter contract with client-specific mechanisms | A&D `07-Integrations/Game Client Integration.md` |
| external clients remain authority for account/license/install facts | Ownership / Data / External Evidence Trust |
| projection evidence retains provenance/freshness | SPEC-03 projection model + Data Ownership |
| client launch acceptance != game running | Interfaces / Game Launch Behavior / Flows |
| client credentials remain external | Trust / Game Client Integration |
| direct game EXE is not generic fallback | Trust + client ownership boundary |
| Steam protocol launch is documented; local library parser version-sensitive | current Valve research + A&D candidate closure |
| Epic protocol activation is documented; local library parser version-sensitive | Epic protocol documentation + A&D candidate closure |
| Microsoft packaged/AUMID activation is Windows-supported but universal Xbox cloud library is not assumed | Windows/GDK research + A&D Xbox OPEN closure |
| Battle.net stays experimental until stable mechanism validated | A&D OPEN + current research |
| process correlation uses evidence proof sets, not parent PID alone | Game Session / SPEC-06 process evidence / Failure model |
| client update can invalidate one capability without all capabilities | Compatibility Management + adapter capability model |
| one managed foreground game at a time | Game Session State Model |
| Game Client process is not Game process | Game Launch behavior/integration |

---

## 2. Specification decisions

```text
SPEC-DEC-054
v1 Game Client integrations use one shared semantic adapter contract with independent per-capability mechanism status.

SPEC-DEC-055
external-client support is capability-scoped; a documented launch mechanism does not imply supported library discovery or license evidence.

SPEC-DEC-056
all GameInstallationProjection evidence carries client/source provenance, freshness and mechanism status; stale evidence never silently remains verified current truth.

SPEC-DEC-057
managed game launch is split into client HANDOFF_ACCEPTED and independent process/application observation before GAME_RUNNING_CONFIRMED.

SPEC-DEC-058
v1 process correlation uses named explainable proof sets and PID+creation identity where Win32 process identity is involved; parent-child process ancestry alone is insufficient.

SPEC-DEC-059
WEAK foreground/timing/filename evidence alone cannot establish GAME_RUNNING.

SPEC-DEC-060
client-managed titles launch through owning client/platform mechanisms by default; direct game executable launch is not a generic fallback.

SPEC-DEC-061
Steam v1 target launch uses Valve-documented steam protocol with Steam App ID, while local VDF/ACF library/install parsing is isolated as VERSION_SENSITIVE/BEST_EFFORT evidence.

SPEC-DEC-062
Epic v1 target launch uses documented com.epicgames.launcher protocol activation; preferred identity is Sandbox/Catalog/Artifact and documented validated-install-path activation is a fallback. Local library metadata remains version-sensitive.

SPEC-DEC-063
Microsoft Gaming v1 support is package/application-registration scoped: PFN+AUMID is preferred stable identity and Windows application activation is preferred launch. Full Xbox/Game Pass cloud library/license enumeration is not claimed.

SPEC-DEC-064
Battle.net remains EXPERIMENTAL until discovery, library/install evidence and launch product-code mechanisms are validated against a supported client matrix; community/forum mechanisms do not become SUPPORTED_PUBLIC by assumption.

SPEC-DEC-065
external Game Client credentials/tokens are never collected or stored by SplitOS adapters; client-owned auth UI remains external.

SPEC-DEC-066
local client metadata is read-only untrusted input and cannot supply arbitrary executable/URI/command/privileged operation surfaces.

SPEC-DEC-067
client compatibility is evaluated per capability. A client update may disable a version-sensitive parser while leaving a public/Windows-supported launch mechanism available.

SPEC-DEC-068
v1 exit correlation uses the correlated game process/application set and ignores persistent external client processes.

SPEC-DEC-069
Runtime Host restart must reconcile a possibly already-started game from current evidence before resubmitting an external launch.

SPEC-DEC-070
client support labels exposed to product/UX distinguish SUPPORTED, PARTIAL, EXPERIMENTAL, STALE_COMPATIBILITY and UNSUPPORTED_VERSION rather than a single boolean.
```

---

## 3. Verification backlog — shared adapter

```text
V-GC-001 capability statuses independent per adapter
V-GC-002 stale projection preserves STALE_LAST_KNOWN
V-GC-003 malformed local metadata becomes parse/unknown failure, not empty verified library
V-GC-004 arbitrary URI/command injection from UI rejected
V-GC-005 direct client-game EXE generic fallback absent
V-GC-006 external passwords/tokens absent from adapters/storage/logs
V-GC-007 HANDOFF_ACCEPTED never writes GAME_RUNNING
V-GC-008 weak foreground/timing evidence cannot confirm running
V-GC-009 PID reuse handled through creation identity
V-GC-010 client process is never correlated as game process by itself
V-GC-011 bootstrap replacement flow
V-GC-012 client remains running after game EXIT_CONFIRMED
V-GC-013 Runtime Host restart reconciles before relaunch
V-GC-014 unknown client version downgrades only affected capabilities
V-GC-015 file-watcher overflow triggers refresh, not fabricated install changes
V-GC-016 path traversal/reparse ambiguity rejected for validated install root
```

---

## 4. Verification backlog — Steam

```text
V-STEAM-001 protocol handler discovery from non-default install path
V-STEAM-002 malformed steam protocol registration rejected
V-STEAM-003 libraryfolders multiple library parsing
V-STEAM-004 unknown/malformed VDF does not become verified empty library
V-STEAM-005 appmanifest external AppID normalization
V-STEAM-006 install-root validation
V-STEAM-007 steam protocol handoff/client closed
V-STEAM-008 client auth/update/intermediate UI does not false-confirm running
V-STEAM-009 generic install-root process proof
V-STEAM-010 curated executable proof
V-STEAM-011 ambiguous process candidates rejected
V-STEAM-012 game exit while Steam remains running
V-STEAM-013 Steam update invalidates parser independently from protocol launch
V-STEAM-014 stale projection blocks FRESH_REQUIRED launch
```

---

## 5. Verification backlog — Epic

```text
V-EPIC-001 documented protocol registration detection
V-EPIC-002 malformed registration rejected
V-EPIC-003 current local manifest schema parser
V-EPIC-004 unknown schema produces UNKNOWN
V-EPIC-005 Sandbox/Catalog/Artifact identity encode/decode
V-EPIC-006 documented validated-install-path launch identity
V-EPIC-007 silent=true still permits client UI without semantic failure
V-EPIC-008 protocol handoff != running
V-EPIC-009 install-root process correlation
V-EPIC-010 publisher launcher replacement
V-EPIC-011 game exit while Epic remains running
V-EPIC-012 launcher update can break parser without breaking protocol capability
```

---

## 6. Verification backlog — Microsoft Gaming

```text
V-MSFT-001 current-user PFN/AUMID installed registration
V-MSFT-002 stable identity survives package version update
V-MSFT-003 package absent/unregistered handling
V-MSFT-004 AUMID application activation in current user session
V-MSFT-005 activation returned PID correlation
V-MSFT-006 packaged process replacement flow
V-MSFT-007 Xbox app remains alive after game exit
V-MSFT-008 install folder move does not break package identity
V-MSFT-009 direct protected EXE launch absent
V-MSFT-010 installed package does not become proactive license-owned assertion
V-MSFT-011 unsupported package model surfaced as partial/unsupported
```

---

## 7. Verification backlog — Battle.net

```text
V-BNET-001 current client discovery non-default path
V-BNET-002 protocol/handler schema validation
V-BNET-003 candidate product-code mapping
V-BNET-004 candidate launch with client closed
V-BNET-005 signed-out/update-required behavior
V-BNET-006 local product/install metadata prototype
V-BNET-007 unknown client schema disables experimental capability
V-BNET-008 per-game process correlation
V-BNET-009 game exit while Battle.net stays alive
V-BNET-010 no arbitrary --exec/product command injection
```

---

## 8. OPEN items intentionally carried forward

SPEC-07 intentionally leaves:

```text
exact Battle.net supported launch/library mechanism
full Xbox/Game Pass cloud library synchronization
universal proactive external platform license checks
per-game curated executable compatibility catalog population
exact default launch/start/exit timeout values
passive adoption of games launched outside SplitOS
```

These must be closed by engineering validation/product decision rather than hidden implementation assumption.

---

## 9. Next target

```text
SPEC-08 Game Profile & Optimization
```

SPEC-08 can now bind profiles to stable SplitOS game IDs and client projections without treating Steam AppID/Epic path/AUMID as the profile's entire domain identity.
