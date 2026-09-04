# SplitOS — Game Client Integration

## 1. Purpose

Документ определяет интеграционную архитектуру SplitOS с внешними игровыми клиентами и платформами.

Canonical semantic boundary уже определён в `06-Interfaces`:

```text
Game Client Availability
Library / Installation Evidence
License / Launch Eligibility Evidence
Launch Handoff
Game Start / Exit Evidence
```

Этот документ отвечает на вопрос:

> Каким техническим способом SplitOS должен получать эти данные и выполнять launch, если разные клиенты имеют разные возможности и не предоставляют единого публичного API?

---

## 2. Adapter architecture

Каноническая схема:

```text
Game Launch / Library domains
          ↓
Game Client Integration Contract
          ↓
┌─────────┼─────────┬─────────┐
↓         ↓         ↓         ↓
Steam    Epic      Xbox     Battle.net
Adapter  Adapter   Adapter   Adapter
          ↓
client-specific mechanisms
```

### Rule

Semantic contract стабилен, client-specific mechanism может меняться.

---

## 3. Adapter capability model

Каждый adapter должен объявлять capabilities, а не притворяться, что умеет всё.

Conceptual capability set:

```text
CLIENT_DISCOVERY
CLIENT_STATE
LIBRARY_DISCOVERY
INSTALLATION_EVIDENCE
LAUNCH_ELIGIBILITY
GAME_LAUNCH
GAME_PROCESS_CORRELATION
GAME_EXIT_CORRELATION
ACCOUNT_CONTEXT_EVIDENCE
UPDATE_STATE_EVIDENCE
```

Example:

```text
Adapter A
LIBRARY_DISCOVERY = SUPPORTED
LAUNCH_ELIGIBILITY = PARTIAL
GAME_LAUNCH = SUPPORTED
GAME_PROCESS_CORRELATION = BEST_EFFORT
```

---

## 4. Integration status classes

```text
SUPPORTED_PUBLIC
SUPPORTED_OS_MECHANISM
BEST_EFFORT_LOCAL_EVIDENCE
VERSION_SENSITIVE
OPEN
UNSUPPORTED
```

These statuses belong to integration capability, not to game ownership.

---

## 5. Client discovery

### General strategy

Use the least fragile evidence available, ordered approximately as:

```text
registered application/package identity
→ supported OS integration/URI handler
→ documented install registration
→ client process evidence
→ version-sensitive local files as best-effort fallback
```

Do not use one hard-coded executable path as canonical client identity.

---

## 6. Library / installation evidence

External Game Client remains authority.

SplitOS stores only:

```text
GameInstallationProjection
```

Adapter reconciliation pattern:

```text
adapter observes client state
→ normalize external game identity
→ emit installation evidence
→ Game Library Representation reconciles projection
→ mark observedAt / freshness
```

### Stale projection rule

If the client is unavailable:

```text
last known installed
```

must not silently become:

```text
currently verified installed
```

Result should preserve uncertainty, for example:

```text
AVAILABLE_VERIFIED
UNAVAILABLE_VERIFIED
STALE_LAST_KNOWN
UNKNOWN
```

Exact enum belongs to Specification later.

---

## 7. Launch integration

Managed launch must distinguish:

```text
Launch request created
→ adapter handoff accepted
→ client processing
→ game process evidence
→ GAME_RUNNING
```

### Rule

```text
client launch mechanism returned success
!= game running
```

The adapter's immediate responsibility ends at handoff semantics.

Game Session lifecycle owns the later interpretation.

---

## 8. Launch mechanism strategy

Preferred order for a client adapter:

```text
1. documented/public client launch mechanism
2. OS-registered URI/protocol mechanism
3. documented command-line mechanism
4. supported shell/application activation
5. carefully validated best-effort fallback
```

Never default to executable injection, DLL injection or anti-cheat-sensitive hooking.

---

# 9. Steam integration

## 9.1 Public integration evidence

Steam exposes public Steamworks/Web APIs primarily for developers/publishers and web-service scenarios. Steam also registers `steam://` protocol handling in normal desktop installations.

### Status

```text
Steam client protocol presence       → SUPPORTED_OS_MECHANISM / needs runtime validation
Steam Web API for local library      → NOT sufficient as universal local-install authority
local Steam files/manifests          → BEST_EFFORT / VERSION_SENSITIVE
```

### Important rule

Do not require a Steamworks publisher key or partner-only API for basic user-side SplitOS game launch/library behavior.

SplitOS is not the publisher of every user's game.

---

## 9.2 Steam client discovery

Candidate evidence:

```text
registered steam:// URI handler
Steam installation registration
running Steam client process
```

Status: `CANDIDATE`.

---

## 9.3 Steam library discovery

Common local Steam metadata/manifests can provide useful installation evidence, but they are not treated here as a guaranteed public stable contract.

Status:

```text
BEST_EFFORT / VERSION_SENSITIVE
```

Therefore adapter must:

- isolate parser logic;
- version/test it;
- fail as `UNKNOWN`, not invent installation truth;
- reconcile after Steam/client changes.

---

## 9.4 Steam launch

Use a client-owned launch mechanism where available, preferably registered protocol/supported client invocation.

Status: `CANDIDATE`, validate exact launch syntax and result semantics during prototype testing.

### Game running evidence

Do not use Steam process alone.

Need game-specific/process correlation after launch.

---

# 10. Epic Games integration

Current research did not establish a stable public end-user local-library/launch API suitable to declare as canonical SplitOS integration.

Therefore:

```text
Epic discovery mechanism      → CANDIDATE
Epic local library evidence   → OPEN / likely version-sensitive
Epic launch mechanism         → OPEN until validated
```

### Rule

Do not canonize community-discovered URI formats or launcher database formats before compatibility validation.

Adapter architecture intentionally allows Epic support to be added without changing internal Game Library/Game Launch contracts.

---

# 11. Xbox / Microsoft Store gaming integration

Xbox/Game Pass titles on Windows can involve packaged and unpackaged application models, Microsoft Store identities, Gaming Services and client/platform state.

### Integration direction

Potential evidence/mechanisms:

```text
Windows package/application identity
App User Model ID where applicable
Windows application activation mechanisms
Xbox app / Gaming Services evidence
```

### Status

`OPEN / CANDIDATE`.

Do not reduce Xbox integration to:

```text
find .exe and run it
```

because packaged-app and platform entitlement semantics can differ.

---

# 12. Battle.net integration

No canonical supported public library/launch integration is assumed yet.

Status:

```text
client discovery      → CANDIDATE
library evidence      → OPEN
launch mechanism      → OPEN
process correlation   → CANDIDATE per game
```

Adapter remains isolated until validated.

---

# 13. Game identity normalization

One logical game may appear through multiple clients.

Example:

```text
Game: Cyberpunk 2077
├── Steam external identity
└── GOG/Epic external identity (future support)
```

SplitOS internal identity must therefore be separate from:

```text
client app id
install path
exe path
```

Adapter output should include:

```text
clientType
externalGameIdentity
normalized metadata evidence
installation evidence
launch identity
```

Game Library Representation decides mapping to canonical SplitOS `Game`.

---

# 14. Process correlation

Game launchers frequently create multi-stage process trees.

Potential pattern:

```text
Launch handoff
→ observe known bootstrap/client process activity
→ discover candidate game process
→ validate against game/client integration metadata
→ mark GAME_RUNNING
```

### Evidence strength

Possible evidence levels:

```text
STRONG
known executable + expected client/game identity

MEDIUM
process/window signature + timing correlation

WEAK
only new foreground process after launch
```

Weak evidence alone should not automatically become canonical running-game truth for supported integrations.

---

# 15. Anti-cheat / DRM boundary

Strict rule:

SplitOS adapters must not require:

```text
DLL injection
memory reading of protected game state
process tampering
anti-cheat bypass
DRM modification
network interception for license emulation
```

Integration should remain outside protected gameplay internals.

---

# 16. Client authentication

SplitOS should normally reuse the user's existing authenticated client session.

It should not collect/store:

```text
Steam password
Epic password
Xbox password
Battle.net password
```

If a client requires authentication during launch, the client remains responsible for presenting its own auth UX.

Result can be normalized as:

```text
AUTH_REQUIRED
```

---

# 17. Client update/version changes

Adapters are version-sensitive integration points.

Each adapter needs compatibility metadata:

```text
adapterVersion
clientVersionObserved
supported/unsupported decision
capabilities
lastValidationDate
knownBreakages
```

Compatibility Management owns the support decision.

---

# 18. Failure model preview

Expected integration failures include:

```text
CLIENT_NOT_INSTALLED
CLIENT_NOT_RUNNING
CLIENT_AUTH_REQUIRED
CLIENT_UPDATE_REQUIRED
CLIENT_VERSION_UNSUPPORTED
LIBRARY_EVIDENCE_UNAVAILABLE
GAME_NOT_INSTALLED
LAUNCH_HANDOFF_FAILED
GAME_PROCESS_NOT_CONFIRMED
STALE_PROJECTION
```

Exact handling belongs to `09-Failures`, but Integration layer must expose enough distinction to handle them.

---

# 19. Steam/web evidence references

Current external references used only to establish available platform directions:

```text
https://partner.steamgames.com/doc/webapi_overview
https://partner.steamgames.com/doc/features/auth
https://learn.microsoft.com/en-us/windows/apps/develop/launch/
```

Important: these sources do **not** establish a universal supported local Steam/Epic/Xbox/Battle.net library API for SplitOS. Missing support remains explicitly OPEN.

---

# 20. v1 integration policy

For v1, a Game Client should be called `SUPPORTED` only when SplitOS can verify at minimum:

```text
client discovery
installation evidence adequate for launcher UX
managed launch
running-game correlation
exit correlation
compatibility behavior across supported client versions
```

If only discovery/launch works but library truth is unreliable, support must be labelled accordingly instead of pretending full support.

---

# 21. Result

Canonical adapter pattern:

```text
External Game Client
       ↓ client-specific evidence/mechanism
Client Adapter
       ↓ normalized semantic contracts
Game Library / Launch Orchestration
       ↓
SplitOS canonical state
```

The next Flow layer will connect adapter calls to Game Profile, hardware, mode transition and Game Session states.