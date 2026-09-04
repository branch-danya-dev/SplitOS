# SPEC-07 — Steam Adapter

## 1. Purpose

Defines the v1 Steam-specific implementation of the shared Game Client Adapter contract.

Steam is the first `TARGET_SUPPORTED_V1` adapter, but full support still depends on empirical compatibility tests for local library metadata across supported Steam client versions.

---

## 2. Capability baseline

| Capability | v1 mechanism | Status |
|---|---|---|
| CLIENT_DISCOVERY | registered `steam:` protocol + validated handler executable | `SUPPORTED_OS_MECHANISM` |
| CLIENT_VERSION_EVIDENCE | handler/client executable file version where available | `SUPPORTED_OS_MECHANISM` / evidence only |
| LIBRARY_DISCOVERY | `libraryfolders.vdf` + per-library app manifests | `VERSION_SENSITIVE` |
| INSTALLATION_EVIDENCE | read-only `appmanifest_<appid>.acf` + install-root validation | `BEST_EFFORT_LOCAL_EVIDENCE` |
| LAUNCH_IDENTITY_RESOLUTION | Steam App ID | `SUPPORTED_PUBLIC` identity |
| GAME_LAUNCH | `steam://run/<appid>` through Windows registered protocol | `SUPPORTED_PUBLIC` |
| LAUNCH_ELIGIBILITY_EVIDENCE | no universal proactive ownership/license check | `OPEN` / client-owned |
| CLIENT_INTERACTION_EVIDENCE | generic client UI/process observation only | `PARTIAL` |
| GAME_PROCESS_CORRELATION | install-root + launch timing + optional game executable catalog | `SUPPORTED_VERSION_GATED` |
| GAME_EXIT_CORRELATION | correlated process set | `SUPPORTED_OS_MECHANISM` |
| ACCOUNT_CONTEXT_EVIDENCE | not required for v1 | `UNSUPPORTED` |
| UPDATE_STATE_EVIDENCE | local metadata may suggest state, not universal public contract | `BEST_EFFORT_LOCAL_EVIDENCE` |

---

## 3. Research basis

Valve Steamworks documentation explicitly uses Steam URLs such as:

```text
steam://run/<appid>
steam://rungameid/<appid>
```

for application launch/launch-parameter scenarios.

Public reference:

```text
https://partner.steamgames.com/doc/api/ISteamApps
```

Important limitation:

Steamworks APIs execute in the context of a Steam-enabled title and are not treated as a universal third-party desktop API for enumerating every game installed in the current user's Steam client.

Therefore SplitOS does not require publisher Steamworks credentials/keys for ordinary user-side library/launch behavior.

---

## 4. Client discovery

Primary discovery:

```text
HKCR / merged Classes registration for `steam` URI scheme
↓
registered open command
↓
normalized handler executable
↓
file exists + expected Steam client image evidence
```

Do not hard-code only:

```text
C:\Program Files (x86)\Steam\steam.exe
```

because Steam may be installed elsewhere.

Discovery result includes:

```text
protocol registered
handler executable path
observed file version if obtainable
client process state separately
```

---

## 5. Protocol handler trust

A registered `steam:` handler is user/machine configuration and therefore external evidence.

Adapter MUST:

- parse the handler command conservatively;
- canonicalize the executable path;
- reject malformed command templates;
- verify the target file exists;
- record observed publisher/signature evidence where implementation supports it;
- never append arbitrary UI-supplied arguments to the handler command.

Game launch itself SHOULD be performed via normal Windows URI activation, not by reconstructing the handler command manually.

---

## 6. Steam install root

The validated Steam executable location may be used to infer the candidate Steam installation root.

Example conceptual relationship:

```text
<SteamRoot>\steam.exe
<SteamRoot>\steamapps\libraryfolders.vdf
```

This relationship is version-sensitive local evidence, not a Valve public API contract.

If expected metadata is absent:

```text
LIBRARY_SOURCE_NOT_FOUND
```

while `GAME_LAUNCH` capability may remain available if the public protocol is valid.

---

## 7. Library folders parser

Candidate source:

```text
<SteamRoot>\steamapps\libraryfolders.vdf
```

Parser rules:

- read-only;
- bounded maximum file size;
- tolerant of ordering/whitespace changes;
- explicit VDF token parser, not regex-only parsing;
- unknown fields ignored unless required for structural navigation;
- schema/parse failure downgrades library evidence, not protocol launch capability;
- library paths canonicalized and deduplicated.

Each discovered library must be validated to contain an expected `steamapps` structure before use.

---

## 8. App manifest parser

Candidate source per Steam library:

```text
<Library>\steamapps\appmanifest_<appid>.acf
```

Useful observed fields include conceptually:

```text
appid
name
installdir
StateFlags / update-related metadata where present
```

Only `appid` is used as stable external game identity.

Other fields are version-sensitive evidence.

Adapter MUST NOT assign semantic meaning to undocumented numeric flags without tested compatibility mapping.

---

## 9. Installation evidence

High-confidence local installation evidence requires at minimum:

```text
valid app manifest
+
manifest appid matches file/external identity
+
valid installdir
+
resolved install directory exists under the declared Steam library
```

Result:

```text
INSTALLED_VERIFIED_EVIDENCE
```

This means:

> current local Steam metadata and filesystem evidence agree that the title appears installed.

It does **not** mean SplitOS owns Steam's canonical install truth.

---

## 10. Incomplete/update states

If manifest/filesystem evidence indicates an incomplete/update/transitional state but the adapter cannot map it reliably:

```text
installState = UNKNOWN or INSTALLING
mechanismStatus = VERSION_SENSITIVE
```

The adapter must not simplify every manifest presence to `ready to launch`.

At launch time Steam remains responsible for update/repair UX.

---

## 11. External identity

Canonical Steam binding:

```text
clientType = STEAM
externalIdKind = STEAM_APP_ID
externalId = decimal AppID string
```

Validation:

- parse as unsigned numeric App ID within implementation range;
- canonical decimal representation;
- no URI fragments/arguments included in external ID.

Example:

```text
1091500
```

not:

```text
steam://run/1091500//arguments
```

---

## 12. Launch identity

Prepared launch identity:

```text
SteamLaunchIdentityV1
{
  appId
}
```

No arbitrary command-line string is part of v1 default Steam launch identity.

Game-specific launch options remain owned by Steam/user unless a later explicitly supported Game Configuration contract defines otherwise.

---

## 13. Launch submission

v1 canonical launch URI:

```text
steam://run/<appid>
```

submitted through Windows URI/shell activation in the interactive user session.

Adapter MUST percent/format validate any URI construction even though App ID is numeric.

Immediate success means:

```text
HANDOFF_ACCEPTED
```

only.

---

## 14. Steam client not already running

The adapter does not require `steam.exe` to already be running.

The registered Steam URI handler is allowed to start/activate Steam as part of handoff.

Therefore:

```text
Steam process absent before launch
!= CLIENT_NOT_AVAILABLE
```

if protocol registration/client installation is verified.

---

## 15. Authentication/update interaction

Steam may show:

- login;
- family/ownership restrictions;
- update/install UI;
- launch-option dialog;
- cloud conflict;
- other client-owned interactions.

The URI dispatch return does not distinguish all cases.

Without strong client-specific evidence SplitOS reports:

```text
CLIENT_INTERACTION_REQUIRED
```

or ultimately:

```text
GAME_PROCESS_NOT_CONFIRMED
```

rather than falsely classifying `AUTH_REQUIRED`.

---

## 16. Process correlation inputs

Adapter derives expected process evidence from:

```text
validated Steam install root for game
+
SplitOS supported-game executable catalog where available
+
process baseline immediately before launch
+
launch timestamp/session
```

The Steam app manifest is not assumed to provide a canonical executable path.

---

## 17. Generic Steam correlation

For a game without a curated executable catalog, acceptable MEDIUM correlation may be:

```text
new process after handoff
+
same Windows user session
+
normalized executable path located under validated Steam game install root
+
process stable for configured start window
```

If multiple plausible executables appear and cannot be distinguished:

```text
CORRELATION_AMBIGUOUS
```

not `GAME_RUNNING`.

---

## 18. Curated correlation

For officially supported high-confidence titles, SplitOS MAY ship a release-owned mapping:

```text
Steam AppID
→ expected executable roles/patterns
→ optional bootstrap/replacement rules
```

Example conceptual roles:

```text
launcher.exe → PUBLISHER_LAUNCHER
game.exe     → GAME_PRIMARY
```

This mapping belongs to SplitOS compatibility knowledge and is versioned independently from Steam manifest parsing.

---

## 19. Steam process is not game process

`steam.exe` / `steamwebhelper.exe` activity after launch never establishes Game Session running state.

They may be retained only as client interaction diagnostics.

---

## 20. Exit correlation

Steam normally remains running after game exit.

Exit rule observes only the correlated game process set:

```text
GAME_PRIMARY / required correlated processes absent
+
no permitted replacement during grace window
→ EXIT_CONFIRMED
```

Steam client process lifetime is ignored for Game Session exit.

---

## 21. Metadata invalidation

Watch candidates:

```text
libraryfolders.vdf
<Library>\steamapps\appmanifest_*.acf
```

Events invalidate related projections.

Because Steam may replace files atomically during writes, watcher logic must handle:

- create/delete/rename/change;
- temporary source unavailability;
- bounded delayed re-read;
- watcher overflow → full Steam refresh.

---

## 22. Steam client update

If Steam client version changes:

```text
protocol capability
→ validate registration; normally remain usable

local metadata parser
→ schema probe
→ continue only if structural validation passes
```

Unknown parser schema yields:

```text
LIBRARY_SCHEMA_UNKNOWN
```

while preserving the ability to launch a known AppID through the public protocol when appropriate.

---

## 23. Stale library behavior

If Steam is unavailable or parser fails:

```text
last known install projection
→ STALE_LAST_KNOWN
```

Game Launcher may display it as stale/needs refresh, but a `FRESH_REQUIRED` managed launch must refresh or fail explicitly.

---

## 24. Security boundary

Steam local metadata MUST NOT supply:

- arbitrary executable launched directly;
- arbitrary Steam command-line arguments;
- privileged filesystem operations;
- Broker service names/policies;
- SplitOS canonical game identity without normalization.

The adapter never edits Steam manifests/library files.

---

## 25. Not supported in v1

```text
Steam account password/token collection
Steam DRM emulation/bypass
automatic ownership inference from filesystem alone
modifying Steam launch options
forcing cloud conflict resolution
Steam internal IPC reverse engineering as supported contract
DLL injection / overlay hooking
```

---

## 26. Verification cases

```text
V-STEAM-001 protocol handler discovery from non-default install path
V-STEAM-002 malformed protocol handler rejected
V-STEAM-003 libraryfolders parser multiple libraries
V-STEAM-004 malformed/unknown VDF fails as unknown, not empty library
V-STEAM-005 appmanifest install evidence validates root
V-STEAM-006 manifest missing install directory does not report verified installed
V-STEAM-007 steam://run handoff accepted does not set GAME_RUNNING
V-STEAM-008 Steam login/update UI times out/interaction without false running
V-STEAM-009 generic install-root process correlation
V-STEAM-010 ambiguous multiple candidates rejected
V-STEAM-011 bootstrap process replacement preserves session when allowed
V-STEAM-012 game exit confirmed while Steam remains running
V-STEAM-013 Steam client update invalidates parser capability independently
V-STEAM-014 stale projection cannot satisfy fresh launch
V-STEAM-015 adapter never launches game exe directly as generic fallback
```

---

## 27. Release gate

Steam becomes `SUPPORTED` in a SplitOS release only after the current Steam client compatibility matrix passes the mandatory tests above on supported Windows builds.

Until then the specification status is:

```text
TARGET_SUPPORTED_V1
```

not a marketing claim of already-verified compatibility.
