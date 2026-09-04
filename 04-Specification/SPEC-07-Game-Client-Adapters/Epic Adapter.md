# SPEC-07 — Epic Games Launcher Adapter

## 1. Purpose

Defines the v1 Epic Games Launcher implementation of the shared Game Client Adapter contract.

Epic is a `TARGET_SUPPORTED_V1` client because Epic documents protocol activation for launching apps, but local library/install discovery remains version-sensitive local evidence and must be isolated accordingly.

---

## 2. Capability baseline

| Capability | v1 mechanism | Status |
|---|---|---|
| CLIENT_DISCOVERY | registered `com.epicgames.launcher` protocol | `SUPPORTED_PUBLIC` / OS registration |
| CLIENT_VERSION_EVIDENCE | registered handler executable/file version | evidence only |
| LIBRARY_DISCOVERY | Epic local launcher metadata/manifests | `VERSION_SENSITIVE` |
| INSTALLATION_EVIDENCE | read-only local launcher manifests + filesystem validation | `BEST_EFFORT_LOCAL_EVIDENCE` |
| LAUNCH_IDENTITY_RESOLUTION | Sandbox/Catalog/Artifact identity or documented install-path protocol identity | `SUPPORTED_PUBLIC` once resolved |
| GAME_LAUNCH | `com.epicgames.launcher://apps/...?...action=launch` | `SUPPORTED_PUBLIC` |
| LAUNCH_ELIGIBILITY_EVIDENCE | external client-owned; no universal proactive API assumed | `OPEN` |
| CLIENT_INTERACTION_EVIDENCE | launcher UX/process observation | `PARTIAL` |
| GAME_PROCESS_CORRELATION | install-root + launch timing + optional executable catalog | `SUPPORTED_VERSION_GATED` |
| GAME_EXIT_CORRELATION | correlated game process set | `SUPPORTED_OS_MECHANISM` |
| ACCOUNT_CONTEXT_EVIDENCE | not required | `UNSUPPORTED` |
| UPDATE_STATE_EVIDENCE | local launcher metadata may expose hints | `BEST_EFFORT_LOCAL_EVIDENCE` |

---

## 3. Public protocol basis

Epic documents Windows protocol activation through:

```text
com.epicgames.launcher://
```

and app launch using:

```text
com.epicgames.launcher://apps/[SandboxID]%3A[CatalogID]%3A[ArtifactId]?action=launch&silent=true
```

Epic also documents using a URL-encoded installation path inside the `apps/` URI as an alternative launch identity.

Public research artifact:

```text
Epic Games Protocol Activation / Deep-Linking
```

The protocol is handled by the Epic Games Launcher through normal OS protocol registration.

---

## 4. Client discovery

Epic's public protocol documentation advises checking Windows registration for:

```text
HKEY_CLASSES_ROOT\com.epicgames.launcher
```

before protocol activation.

Adapter discovery therefore checks:

```text
registered protocol
↓
registered handler command/executable evidence
↓
normalized executable path
↓
file/version evidence where available
```

The adapter MUST prefer normal protocol activation for launch rather than manually invoking an extracted command template.

---

## 5. Protocol registration trust

The registry entry is external local configuration.

Adapter MUST:

- treat handler command as untrusted input;
- canonicalize any discovered executable path;
- reject malformed handler registration;
- avoid passing arbitrary arguments copied from UI;
- launch only the adapter-generated Epic URI through OS activation.

---

## 6. External Epic identity

Preferred identity:

```text
clientType = EPIC
externalIdKind = EPIC_SANDBOX_CATALOG_ARTIFACT
externalId = SandboxID : CatalogID : ArtifactId
```

The three components MUST be stored separately in the typed payload even if projection serialization also exposes a canonical joined form.

Deprecated ArtifactId-only identities MAY be imported as compatibility evidence but MUST NOT be the preferred new binding.

---

## 7. Local library evidence

Epic does not provide SplitOS a public universal desktop library-enumeration API in the current baseline.

Therefore library/install discovery uses read-only launcher metadata as:

```text
VERSION_SENSITIVE
BEST_EFFORT_LOCAL_EVIDENCE
```

Candidate metadata roots may include Epic launcher installation/manifests under `%ProgramData%`.

Exact filenames/schema are adapter-version implementation details and MUST be covered by compatibility tests.

---

## 8. Manifest parser contract

The parser may extract only validated fields needed for normalized evidence, for example conceptually:

```text
Sandbox / Namespace identity
Catalog / Item identity
Artifact / App identity
InstallLocation
LaunchExecutable evidence
DisplayName evidence
installation/update state hints
```

Rules:

- unknown fields ignored;
- unknown required structure yields `LIBRARY_SCHEMA_UNKNOWN`;
- no raw manifest object crosses the adapter boundary;
- no manifest-supplied executable is launched directly by default;
- local path evidence is canonicalized and validated.

---

## 9. Installation evidence

High-confidence evidence requires:

```text
valid Epic metadata record
+
resolved external identity or documented path launch identity
+
canonicalized install root exists
+
expected installation structure evidence
```

Result:

```text
INSTALLED_VERIFIED_EVIDENCE
```

It remains a local projection of Epic's state, not canonical platform truth.

---

## 10. Launch identity strategies

The adapter supports two documented launch identities.

### Strategy A — Product identity

```text
SandboxID
CatalogID
ArtifactId
```

URI:

```text
com.epicgames.launcher://apps/<encoded triple>?action=launch&silent=true
```

### Strategy B — Validated install path

A URL-encoded location inside the game's validated install directory as allowed by Epic protocol documentation.

This is fallback-only when the product identity triple cannot be resolved reliably.

The path MUST come from fresh validated installation evidence.

---

## 11. Launch identity preference

Priority:

```text
validated Sandbox/Catalog/Artifact
→ validated documented install-path activation
→ otherwise LAUNCH_IDENTITY_MISSING
```

The adapter MUST NOT invent codenames/artifact IDs from display names.

---

## 12. Launch submission

Launch is submitted through OS URI activation:

```text
Shell/Windows protocol activation
→ com.epicgames.launcher://apps/...
```

The `silent=true` flag is advisory according to Epic documentation; the launcher may still show UI when necessary.

Therefore:

```text
URI dispatch accepted
→ HANDOFF_ACCEPTED
```

not `GAME_RUNNING`.

---

## 13. Launcher not running

The Epic protocol handler may activate/start the Epic Games Launcher.

Thus:

```text
EpicGamesLauncher process absent
!= CLIENT_NOT_AVAILABLE
```

when the registered protocol is valid.

---

## 14. Client interaction

Epic may show UI for:

- sign-in;
- update/install;
- consent;
- publisher launcher;
- cloud conflict;
- other client-required flows.

Because `silent=true` does not guarantee invisible execution, SplitOS must keep Game Launcher/session state tolerant of client foreground interaction.

If exact reason is not proven:

```text
CLIENT_INTERACTION_REQUIRED
```

is used.

---

## 15. Process correlation

Expected process evidence derives from:

```text
validated install root
+
manifest executable evidence (read-only hint)
+
SplitOS supported-game executable catalog where available
+
pre-launch process baseline
+
launch timestamp/session
```

Manifest executable fields are evidence, not direct launch commands.

---

## 16. Generic Epic correlation

MEDIUM acceptable proof may include:

```text
new process after handoff
+
same user session
+
image path under validated Epic install root
+
not merely EpicGamesLauncher.exe
+
stable for configured interval
```

If a publisher launcher starts first, it may be classified as `PUBLISHER_LAUNCHER` and observation continues for the actual game process.

---

## 17. Curated Epic game correlation

Officially supported titles MAY have release-owned compatibility entries:

```text
Epic external identity
→ expected executable roles
→ bootstrap replacements
→ expected publisher launcher behavior
```

This catalog is SplitOS compatibility knowledge and is not written back into Epic metadata.

---

## 18. Epic client process is not game process

The following never proves game running:

```text
EpicGamesLauncher.exe running
EpicWebHelper/helper processes running
```

Only correlated game/publisher process evidence may drive Game Session observation.

---

## 19. Exit correlation

Epic Launcher normally survives game exit.

Exit confirmation ignores launcher processes and tracks only the correlated game process set plus permitted replacements.

---

## 20. Local metadata invalidation

Adapter MAY watch validated Epic manifest metadata directories.

Any event:

```text
manifest create/change/delete/rename
→ Epic projection generation invalidated
→ bounded refresh
```

File watcher events do not directly assert install/uninstall.

---

## 21. Client version change

Protocol activation is treated independently from local metadata parsing.

After Epic client update:

```text
protocol registration valid
→ GAME_LAUNCH may remain available

manifest schema changed
→ LIBRARY_DISCOVERY becomes UNKNOWN/VERSION_SENSITIVE
```

This separation is mandatory.

---

## 22. Deprecated protocol identity

Epic documentation notes an older ArtifactId-only URI form may remain supported for backward compatibility.

SplitOS MAY read/import such evidence but SHOULD normalize to the modern product triple when the launcher metadata provides it.

New profiles/bindings SHOULD NOT intentionally depend on deprecated form.

---

## 23. Security boundary

Adapter MUST NOT:

- directly execute a manifest-provided game executable as generic fallback;
- pass unvalidated install paths into URI construction;
- copy arbitrary query parameters from local metadata/UI;
- edit Epic launcher manifest files;
- access Epic account credentials/tokens;
- inject into Epic/game processes.

---

## 24. Not supported in v1

```text
Epic account password/token collection
EOS publisher/private APIs as requirement for all user games
store purchase automation
license emulation
direct manifest/database mutation
automatic resolution of every client UI state
```

---

## 25. Verification cases

```text
V-EPIC-001 public protocol registration discovered
V-EPIC-002 malformed handler rejected
V-EPIC-003 local metadata parser normal current schema
V-EPIC-004 unknown manifest schema produces UNKNOWN, not empty library
V-EPIC-005 external triple normalized correctly
V-EPIC-006 install-path identity canonicalized/validated
V-EPIC-007 product-triple protocol URI encoded safely
V-EPIC-008 path-based protocol URI encoded safely
V-EPIC-009 HANDOFF_ACCEPTED does not set GAME_RUNNING
V-EPIC-010 launcher UI despite silent=true handled as client interaction
V-EPIC-011 install-root process correlation
V-EPIC-012 publisher launcher replacement flow
V-EPIC-013 Epic client remains running after EXIT_CONFIRMED
V-EPIC-014 client update can invalidate library parser without disabling documented protocol
V-EPIC-015 stale manifest evidence cannot satisfy fresh launch
```

---

## 26. Release gate

Epic becomes `SUPPORTED` only after both:

```text
public protocol launch tests
+
current client metadata compatibility tests
+
process/exit correlation tests
```

pass on the release-supported Windows/client matrix.
