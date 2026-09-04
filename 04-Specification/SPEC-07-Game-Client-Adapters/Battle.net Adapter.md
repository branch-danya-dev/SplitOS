# SPEC-07 — Battle.net Adapter

## 1. Purpose

Defines the current v1 Battle.net adapter posture.

Unlike Steam/Epic, the current research baseline does not establish a stable, public, universal Battle.net desktop library/game-launch contract suitable to declare as a fully supported SplitOS dependency.

Therefore:

```text
Battle.net Adapter = EXPERIMENTAL
```

until engineering validation closes the remaining mechanism OPENs.

---

## 2. Capability baseline

| Capability | Current direction | Status |
|---|---|---|
| CLIENT_DISCOVERY | registered Battle.net/Blizzard protocol and/or validated installed application registration | `VERSION_SENSITIVE` |
| CLIENT_VERSION_EVIDENCE | executable/file version | evidence only |
| LIBRARY_DISCOVERY | local Battle.net/Agent metadata | `OPEN / VERSION_SENSITIVE` |
| INSTALLATION_EVIDENCE | local product metadata + filesystem evidence | `OPEN / BEST_EFFORT_LOCAL_EVIDENCE` |
| LAUNCH_IDENTITY_RESOLUTION | Blizzard product code where validated | `VERSION_SENSITIVE` |
| GAME_LAUNCH | protocol/`--exec` mechanisms observed in Blizzard ecosystem but not accepted as stable public API yet | `VERSION_SENSITIVE / OPEN` |
| LAUNCH_ELIGIBILITY_EVIDENCE | Battle.net-owned | `OPEN` |
| CLIENT_INTERACTION_EVIDENCE | launcher UI/process observation | `PARTIAL` |
| GAME_PROCESS_CORRELATION | per-game executable/installation evidence | `EXPERIMENTAL` |
| GAME_EXIT_CORRELATION | correlated game process set | `SUPPORTED_OS_MECHANISM` once correlation exists |
| ACCOUNT_CONTEXT_EVIDENCE | not required | `UNSUPPORTED` |
| UPDATE_STATE_EVIDENCE | Battle.net-owned | `OPEN` |

---

## 3. Why Battle.net is not called supported yet

Community and Blizzard forum material show launch patterns such as conceptually:

```text
battlenet://<ProductCode>
Battle.net.exe --exec="launch <ProductCode>"
```

and current Battle.net product configuration contains registered `battlenet` / `blizzard` URI handlers.

However SplitOS has not established a maintained public vendor contract guaranteeing:

- product code catalog semantics;
- launch syntax stability;
- library enumeration format;
- install-state schema;
- cross-version behavior.

Therefore these mechanisms remain isolated behind the adapter and are not canonical platform contracts.

---

## 4. Client discovery

Candidate evidence:

```text
registered battlenet/blizzard URI handler
validated Battle.net executable registration
running Battle.net process evidence
```

Rules:

- a hard-coded default installation path is insufficient;
- protocol handler command is untrusted external configuration;
- executable path is normalized/validated;
- client process absence does not prove client unavailable;
- all discovery mechanisms remain compatibility-tested per release.

---

## 5. Library/install research boundary

Battle.net local product/install metadata is not accepted as a stable public schema in this spec.

A prototype MAY read local metadata in a strictly read-only parser module, but until validation is complete the result must be:

```text
BEST_EFFORT_LOCAL_EVIDENCE
```

or:

```text
UNKNOWN
```

not release-supported canonical installation evidence.

---

## 6. Product identity

Potential external identity:

```text
BattleNetProductIdentityV1
{
  productCode
  regionVariant?
  installIdentityHint?
}
```

`productCode` MUST NOT be inferred from localized display name.

Each supported code would require release-owned compatibility knowledge sourced from validated Battle.net metadata/mechanisms.

---

## 7. Launch mechanisms under evaluation

Candidate A:

```text
battlenet://<productCode>
```

Candidate B:

```text
Battle.net.exe --exec="launch <productCode>"
```

Current specification does **not** choose either as `SUPPORTED_PUBLIC`.

Before promotion, prototype tests must establish:

- how handler/executable is discovered;
- exact quoting/encoding rules;
- current product code mapping;
- behavior when client is closed;
- behavior when auth required;
- behavior when update required;
- whether command returns before client/game starts;
- compatibility across current Battle.net releases.

---

## 8. No direct game executable fallback

Battle.net games frequently rely on Battle.net/Agent authentication/update context.

SplitOS MUST NOT respond to missing supported launch mechanism by simply running:

```text
<GameInstall>\game.exe
```

as a generic supported fallback.

If a particular Blizzard title officially supports direct launch, that must be a separate per-game supported contract.

---

## 9. Client interaction

If an experimental launch opens Battle.net but no game process appears:

```text
HANDOFF_ACCEPTED / CLIENT_INTERACTION_REQUIRED
```

may be reported only according to evidence.

The adapter MUST NOT guess:

```text
AUTH_REQUIRED
```

from mere absence of a game process.

---

## 10. Process correlation

Correlation uses:

```text
validated installation evidence where available
+
release-owned per-game executable knowledge
+
same Windows session
+
launch timing
+
normal SPEC-06 process evidence
```

Battle.net itself remaining foreground/background does not establish Game Session running state.

---

## 11. Multi-stage launchers

Blizzard titles may have title launchers/bootstrap behavior.

Adapter must support roles:

```text
BATTLENET_CLIENT
PUBLISHER_LAUNCHER
GAME_PRIMARY
GAME_SECONDARY
```

and may track permitted replacements only when validated for that product.

---

## 12. Exit correlation

After a game exits, Battle.net may remain running.

Therefore exit confirmation uses only the correlated game process set.

Battle.net process lifetime is ignored for `GAME_EXITED_CONFIRMED`.

---

## 13. Version sensitivity

Battle.net client update invalidates all unversioned assumptions.

Adapter compatibility record must include:

```text
observed Battle.net client version
adapterVersion
validated discovery mechanism
validated launch mechanism if any
validated product codes
validated local metadata schema signature
lastValidatedUtc
```

Unknown client version may downgrade the adapter to discovery-only/unsupported rather than executing an unvalidated command.

---

## 14. Parser security

Experimental local metadata parser MUST:

- be read-only;
- cap file size/nesting;
- reject malformed records;
- canonicalize paths;
- never execute metadata-derived command lines;
- never write Battle.net/Agent files;
- never access credential stores;
- return stale/unknown on schema mismatch.

---

## 15. Promotion criteria

Battle.net can move from `EXPERIMENTAL` to `TARGET_SUPPORTED_V1` only when ENG/SPEC verification proves at minimum:

```text
CLIENT_DISCOVERY
INSTALLATION_EVIDENCE
LAUNCH_IDENTITY_RESOLUTION
GAME_LAUNCH
GAME_PROCESS_CORRELATION
GAME_EXIT_CORRELATION
```

against the supported Battle.net client matrix.

---

## 16. Verification backlog

```text
V-BNET-001 registered protocol/executable discovery current client
V-BNET-002 non-default install path
V-BNET-003 candidate launch mechanism with client closed
V-BNET-004 candidate launch mechanism with client signed out
V-BNET-005 candidate launch mechanism with game update required
V-BNET-006 validated product code mapping
V-BNET-007 local metadata schema parsing
V-BNET-008 schema/client update fails closed for supported capability
V-BNET-009 game process correlation
V-BNET-010 Battle.net remains running after game exit
V-BNET-011 direct EXE fallback absent
V-BNET-012 arbitrary product/command injection rejected
```

---

## 17. User-facing support behavior

Until promotion:

- Battle.net games MAY appear as experimental if local discovery succeeds;
- Game Launcher MUST clearly distinguish experimental/unsupported launch;
- SplitOS MUST NOT advertise Battle.net as a fully supported client;
- inability to manage-launch Battle.net game does not affect normal Windows/Battle.net use.

---

## 18. Result

The adapter boundary is intentionally present now so future Battle.net support does not require changes to Game Library/Game Launch semantics.

```text
Battle.net specific mechanism
→ experimental adapter
→ normalized SplitOS contract
```

but unsupported assumptions are not promoted into product truth.
