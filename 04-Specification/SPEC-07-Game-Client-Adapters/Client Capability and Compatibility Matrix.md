# SPEC-07 — Client Capability and Compatibility Matrix

## 1. Purpose

Provides the normative v1 support matrix for external Game Client integrations.

This file is a release/specification view, not a marketing promise.

A client is supported only to the extent that the release-validated capability matrix says it is.

---

## 2. Capability matrix

| Capability | Steam | Epic | Microsoft Gaming | Battle.net |
|---|---|---|---|---|
| CLIENT_DISCOVERY | `SUPPORTED_OS_MECHANISM` | `SUPPORTED_PUBLIC` protocol registration | `SUPPORTED_OS_MECHANISM` package/app registration | `VERSION_SENSITIVE` |
| CLIENT_VERSION_EVIDENCE | file/version evidence | file/version evidence | package version evidence | file/version evidence |
| LIBRARY_DISCOVERY | `VERSION_SENSITIVE` local VDF/ACF | `VERSION_SENSITIVE` local metadata | `PARTIAL` local registered packages only | `OPEN` |
| INSTALLATION_EVIDENCE | `BEST_EFFORT_LOCAL_EVIDENCE` | `BEST_EFFORT_LOCAL_EVIDENCE` | `SUPPORTED_OS_MECHANISM` for supported registered package model | `OPEN/BEST_EFFORT` |
| LAUNCH_IDENTITY | Steam App ID | Epic product triple / validated path | PFN + AUMID | product code experimental |
| GAME_LAUNCH | `SUPPORTED_PUBLIC` steam protocol | `SUPPORTED_PUBLIC` Epic protocol | `SUPPORTED_OS_MECHANISM` AUMID activation | `OPEN/VERSION_SENSITIVE` |
| PROACTIVE LICENSE CHECK | `OPEN/UNSUPPORTED` | `OPEN/UNSUPPORTED` | `OPEN/UNSUPPORTED` | `OPEN/UNSUPPORTED` |
| AUTH_REQUIRED CLASSIFICATION | generic interaction unless strong signal | generic interaction unless strong signal | generic platform interaction | generic interaction |
| PROCESS CORRELATION | install root + optional catalog | install root + optional catalog | AUMID/package + process | per-game experimental |
| EXIT CORRELATION | supported after process correlation | supported after process correlation | supported after process correlation | experimental |

---

## 3. Client-level posture

```text
Steam              TARGET_SUPPORTED_V1
Epic               TARGET_SUPPORTED_V1
Microsoft Gaming   PARTIAL_SUPPORTED_V1
Battle.net         EXPERIMENTAL
```

`TARGET_SUPPORTED_V1` means specification target pending release verification.

It does not mean every future client version is automatically supported.

---

## 4. Mechanism provenance classes

Every mechanism in release compatibility data carries:

```text
VENDOR_DOCUMENTED
WINDOWS_DOCUMENTED
LOCAL_FORMAT_VALIDATED
COMMUNITY/RESEARCH_ONLY
UNKNOWN
```

Mapping to support status:

```text
VENDOR_DOCUMENTED
→ candidate SUPPORTED_PUBLIC

WINDOWS_DOCUMENTED
→ candidate SUPPORTED_OS_MECHANISM

LOCAL_FORMAT_VALIDATED
→ VERSION_SENSITIVE / BEST_EFFORT_LOCAL_EVIDENCE

COMMUNITY/RESEARCH_ONLY
→ cannot become normal supported mechanism without explicit empirical promotion
```

---

## 5. Research basis — Steam

Public Valve evidence establishes Steam URL launch semantics, including `steam://run/<appid>` usage.

Reference:

```text
https://partner.steamgames.com/doc/api/ISteamApps
```

What this supports:

```text
Steam protocol launch direction
Steam App ID as platform application identity
```

What it does **not** support for SplitOS:

```text
a universal third-party API for enumerating every local Steam install
```

Therefore `libraryfolders.vdf` / `appmanifest_*.acf` remain version-sensitive local evidence despite broad practical use.

---

## 6. Research basis — Epic

Epic documents protocol activation:

```text
com.epicgames.launcher://apps/<identity>?action=launch&silent=true
```

with identity forms including:

```text
SandboxID : CatalogID : ArtifactId
```

and a URL-encoded install path alternative.

Epic documentation also explicitly describes checking Windows registration of:

```text
HKEY_CLASSES_ROOT\com.epicgames.launcher
```

Reference artifact:

```text
Epic Games Protocol Activation (Deep-Linking)
```

What remains unsupported by the public document:

```text
universal local library enumeration schema
```

so launcher manifests remain version-sensitive evidence.

---

## 7. Research basis — Microsoft Gaming

Windows documents:

- Application User Model ID (AUMID) as installed app identity;
- PackageManager/current-user package registration APIs;
- `IApplicationActivationManager::ActivateApplication` for AUMID application activation;
- Microsoft GDK app-launch requirements and game package identity.

References:

```text
https://learn.microsoft.com/en-us/windows/configuration/store/find-aumid
https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication
https://learn.microsoft.com/en-us/gaming/gdk/docs/features/common/game-config/microsoftgameconfig-overview
```

This supports a strong local installed/launch path for compatible registered titles.

It does not establish a universal SplitOS API for the user's complete cloud Xbox/Game Pass library or proactive subscription/license checks.

---

## 8. Research basis — Battle.net

Current Blizzard ecosystem evidence shows registered Battle.net/Blizzard URI handling and historical/current launch patterns discussed by Blizzard users/staff, but the current research did not establish a stable vendor-published universal desktop integration contract equivalent to Epic's protocol document.

Therefore:

```text
Battle.net launch/library
→ VERSION_SENSITIVE / OPEN
→ EXPERIMENTAL
```

Community/forum evidence can guide ENG prototypes but cannot by itself promote the capability to `SUPPORTED_PUBLIC`.

---

## 9. Release compatibility record

Every release stores/test-generates a matrix conceptually:

```text
GameClientReleaseCompatibility
{
  splitosReleaseId
  clientType
  adapterVersion
  supportedWindowsBuilds
  testedClientVersions[]
  capabilityStatuses
  localSchemaSignatures[]
  lastValidatedUtc
  knownIssues[]
}
```

This matrix is versioned SplitOS compatibility knowledge.

---

## 10. Runtime compatibility evaluation

At Runtime:

```text
adapter compiled capability
+
observed client/version/schema
+
release compatibility matrix
→ effective capability
```

Examples:

### Steam update changes VDF structure

```text
GAME_LAUNCH = still SUPPORTED_PUBLIC if steam protocol valid
LIBRARY_DISCOVERY = UNKNOWN / disabled parser
```

### Epic manifest changes but protocol remains registered

```text
GAME_LAUNCH known identity = available
LIBRARY refresh = schema unknown
```

### Microsoft package identity remains but Xbox app updates

```text
AUMID activation capability may remain valid
```

because game activation is anchored in Windows registration, not Xbox app UI version.

---

## 11. Unknown client version policy

A client update does not automatically mean full adapter failure.

Capability-specific policy:

```text
public/documented protocol
→ runtime registration validation
→ MAY continue

version-sensitive parser
→ schema compatibility check
→ fail/downgrade if unknown

experimental CLI/URI
→ disable unless version explicitly validated
```

---

## 12. Support labels exposed to Game Launcher

Normalized UI support status:

```text
SUPPORTED
PARTIAL
EXPERIMENTAL
STALE_COMPATIBILITY
UNSUPPORTED_VERSION
CLIENT_NOT_INSTALLED
```

The Game Launcher uses this status to explain why a title may be visible but not managed-launchable.

---

## 13. Known game support vs client support

Client support and per-game support are separate axes.

Example:

```text
Steam adapter supported
+
unknown game's executable correlation generic only
→ title may be SUPPORTED_GENERIC

Steam adapter supported
+
release-owned Cyberpunk executable rules
→ title may be SUPPORTED_CURATED
```

The exact game/profile compatibility tier is refined by SPEC-08/09.

---

## 14. Mandatory release gates

Before a client capability can be labeled supported:

```text
mechanism contract exists
adapter tests pass
client version/schema compatibility tested
negative/failure cases tested
process start/exit correlation tested
no trust-boundary bypass found
```

For version-sensitive local formats, tests MUST include at least:

- current production client;
- migration/update from previous tested client where practical;
- malformed/truncated metadata;
- metadata written concurrently by client;
- unknown fields/schema evolution;
- non-default installation/library locations.

---

## 15. Support withdrawal

If a client update breaks a critical capability:

```text
release compatibility update / runtime observation
→ capability status downgraded
→ affected title shows degraded/unsupported state
```

SplitOS MUST prefer truthful degradation over silently using stale parser assumptions.

Normal external client use through Windows remains unaffected.

---

## 16. Result

The compatibility matrix preserves a stable product even when external launchers evolve:

```text
external client changes
↓
only affected adapter capability degrades
↓
Game Library/Launch semantic contracts remain stable
```
