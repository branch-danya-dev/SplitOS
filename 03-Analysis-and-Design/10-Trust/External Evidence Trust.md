# SplitOS — External Evidence Trust

## 1. Purpose

Документ определяет, как SplitOS должен относиться к данным и сигналам из внешних систем:

- Windows / drivers / devices;
- Steam / Epic / Xbox / Battle.net;
- Payment Provider;
- browser/custom URI callbacks;
- Microsoft Windows source/update metadata;
- local external-client files/metadata.

Ключевой принцип:

```text
External evidence
!= SplitOS canonical truth
```

если внешний источник не является authority именно для этого факта.

---

## 2. Evidence classes

### Authoritative external evidence

Источник реально владеет фактом.

Примеры:

```text
Payment Provider
→ payment transaction state

Game Client / Platform
→ platform-owned install/license/account truth

Windows/driver/device
→ actual current platform/device state
```

### Derived external evidence

Источник предоставляет наблюдаемый сигнал, который SplitOS должен интерпретировать.

Примеры:

```text
process exists
window exists
client manifest file exists
custom URI arrived
```

### Best-effort evidence

Version-sensitive/local metadata, которое может быть полезно, но не гарантируется стабильным public contract.

Пример:

```text
Steam local manifest parsing
```

---

## 3. Windows actual-state evidence

SplitOS доверяет поддерживаемым Windows APIs как platform evidence при условии platform integrity.

Примеры:

- current session/SID;
- active processes;
- display topology/modes;
- audio devices;
- input devices;
- service state;
- power scheme.

Но Windows API evidence отвечает только на конкретный вопрос.

```text
process running
```

не означает:

```text
application data safely saved
```

И:

```text
SetDisplayConfig returned success
```

не означает:

```text
target display configuration verified
```

---

## 4. Hardware/device data

Device names, EDID-derived labels, driver strings and dynamic capability data являются external input.

Они не должны использоваться напрямую как:

- filesystem path;
- command line;
- privileged identifier;
- trusted HTML/UI markup;
- authorization decision.

Device identity matching should use normalized/stable platform identifiers where possible and tolerate re-enumeration.

---

## 5. Game Client authority

External Game Client/platform owns facts such as:

- user authentication at that platform;
- game ownership/license where applicable;
- platform installation state;
- platform-required updates;
- cloud/platform state.

SplitOS owns:

- unified game projection;
- Game Profile;
- managed launch transaction;
- Game Session interpretation.

Therefore:

```text
Steam says game installed
→ update SplitOS projection/evidence
```

not:

```text
Steam writes SplitOS GameProfile
```

---

## 6. Local Game Client metadata

Local files such as manifests/databases/configs must be treated as parser inputs, not trusted executable instructions.

Required defensive handling:

- bounded file size where practical;
- strict parser/schema expectations;
- no code execution from metadata;
- path normalization;
- reject traversal/unexpected device paths;
- tolerate malformed/stale records;
- version/capability tagging;
- no promotion to privileged operation without independent validation.

---

## 7. Game executable paths

External client may provide/discover a game executable/path.

This path can be used for normal user-session launch only after validation appropriate to launch integration.

It must not flow into Broker as arbitrary executable command.

```text
GameClient metadata
→ Runtime Host adapter
→ normalized launch target
→ normal client/user-session launch mechanism
```

not:

```text
metadata path
→ LocalSystem Broker
→ CreateProcess elevated
```

---

## 8. Game process correlation

Process evidence is heuristic/semantic evidence for `GAME_RUNNING`.

A process name alone is insufficient in many cases.

Potential correlation signals:

- expected executable/path;
- process ancestry/client handoff timing;
- window/process lifecycle;
- game-specific integration evidence;
- client state.

Exact per-client strategy remains adapter capability.

False positive must not grant privilege or entitlement.

---

## 9. Client authentication required

External client result such as:

```text
AUTH_REQUIRED
```

is authoritative for its own client session need, but not a SplitOS security failure.

SplitOS should:

- surface requirement;
- keep Game Mode coherent;
- allow user to authenticate in official client;
- retry/reconcile.

SplitOS should not collect client passwords itself.

---

## 10. Browser/custom URI callbacks

Any callback parameters from browser/custom URI handler are untrusted input until correlated and validated.

Examples:

```text
splitos://auth-callback?... 
splitos://checkout-complete?...
```

Callback can signal:

```text
resume transaction / refresh backend state
```

It cannot authoritatively state:

```text
account = X
entitlement = PRO
payment = SUCCESS
```

---

## 11. Payment Provider evidence

Payment Provider is authority for transaction result, but only backend-to-provider authenticated evidence should affect entitlement decision.

Desktop/browser evidence is UX-only.

SplitOS Backend validates:

- provider authenticity;
- transaction/order identity;
- replay/idempotency;
- expected product mapping;
- final payment state.

Then Product Identity & Entitlement updates entitlement.

---

## 12. Microsoft Windows source

Windows source is external build input.

SplitOS trusts Microsoft as source authority only after source identity/provenance validation appropriate to selected acquisition method.

User-provided file names, download page labels or checksums from unknown third-party sites are not sufficient evidence.

Exact approved acquisition paths remain legal/engineering OPEN.

---

## 13. Windows Update evidence

Microsoft update metadata/packages are external platform inputs.

SplitOS Compatibility Management still owns:

```text
supported for SplitOS release?
```

Thus:

```text
Microsoft offers update X
```

does not imply:

```text
SplitOS should apply X immediately
```

---

## 14. Network responses

All external service payloads are parsed as untrusted network input even over authenticated TLS.

TLS establishes transport peer/channel, not correctness of every field.

Required principles:

- bounded payloads;
- schema/version validation;
- reject unexpected critical values;
- timeouts;
- correlation IDs where relevant;
- no raw response string passed to shell/registry/service commands.

---

## 15. Staleness and freshness

External evidence should carry freshness metadata when material.

Examples:

```text
HardwareSnapshot.capturedAt
GameInstallationProjection.lastObservedAt
EntitlementAssertion.validUntil
CompatibilityDecision.version
```

Consumers must not silently treat stale evidence as current when flow requires fresh state.

---

## 16. Contradictory evidence

Example:

```text
SplitOS cache: game installed
Steam current evidence: game missing
```

External authoritative current evidence wins for install truth; SplitOS projection is reconciled.

Example:

```text
Game Profile target display = TV
Windows current evidence: TV absent
```

Profile remains user-owned intent, but effective runtime target must be re-resolved.

Do not delete user intent merely because current evidence differs.

---

## 17. Unknown/unsupported evidence

Unknown external state should become explicit:

```text
UNKNOWN
UNAVAILABLE
STALE
UNSUPPORTED
AUTH_REQUIRED
```

not guessed success/failure.

This allows controlled fallback and better diagnostics.

---

## 18. Parsing isolation

Version-sensitive external metadata parsers are a higher-risk input surface.

Preferred isolation principles:

- per-client adapter module;
- no privileged process parsing if avoidable;
- strict exception containment;
- adapter failure cannot crash core state owner;
- no shared arbitrary deserialization format from external source;
- fuzz/robustness testing later in Verification.

---

## 19. External content in UI

Game names, paths, device labels, account display names and external error text may be shown in UI but should be escaped/rendered as data.

No external string should become executable markup/script/command.

---

## 20. Evidence normalization

Adapters should normalize vendor-specific evidence into bounded SplitOS semantic types.

Example:

```text
Steam-specific result
→ GameClientLaunchResult {
    status: HANDOFF_ACCEPTED | AUTH_REQUIRED | FAILED | UNKNOWN
    clientId
    externalGameId
    evidenceTimestamp
  }
```

This reduces vendor-specific raw data propagation through core system.

---

## 21. Trust status metadata

Useful normalized metadata:

```text
source
sourceVersion
capturedAt
freshness
confidence/status
integrationMechanismStatus
```

But avoid pretending numeric confidence can replace explicit authority semantics.

---

## 22. Security invariants

### EXT-INV-001

External evidence may update only data/state owned by the consuming semantic owner.

### EXT-INV-002

Game Client metadata is never direct privileged command input.

### EXT-INV-003

Browser/custom URI parameters cannot grant identity/entitlement/payment success directly.

### EXT-INV-004

Platform process evidence does not prove safe application state.

### EXT-INV-005

Microsoft update availability does not bypass SplitOS compatibility decision.

### EXT-INV-006

Malformed external metadata must fail inside adapter boundary without corrupting canonical state.

### EXT-INV-007

Stale evidence must remain distinguishable from fresh evidence.

### EXT-INV-008

Current external evidence may invalidate a projection but must not silently erase user-owned intent/profile data.

---

## 23. Open questions

- exact Steam discovery/manifest parser contract;
- Epic/Xbox/Battle.net supported mechanisms;
- stable game process correlation strategy;
- executable/path validation rules per client;
- exact approved Microsoft source acquisition paths;
- Windows update source/orchestration implementation;
- adapter sandbox/process isolation need;
- external error message sanitization policy;
- maximum payload/file size limits;
- evidence freshness TTLs per domain.

---

## 24. Result

Canonical external evidence chain:

```text
External source
→ bounded adapter/parser
→ validate/normalize
→ preserve authority + freshness metadata
→ semantic owner interprets evidence
→ canonical projection/state changes only if justified
```

This prevents external systems from accidentally becoming hidden owners of SplitOS internal truth.