# SPEC-03 — Physical Storage Layout

## 1. Purpose

Defines the physical Windows directory/database layout and access boundaries for SplitOS local persistence.

---

## 2. Canonical directory tree

```text
%ProgramData%\SplitOS\
├── Data\
│   └── machine.db
├── Backups\
│   └── machine\
├── Staging\
└── Logs\                  # detailed policy in SPEC-13

%LocalAppData%\SplitOS\
├── Data\
│   └── user.db
├── Cache\
│   └── projection.db
├── Backups\
│   └── user\
└── Logs\                  # detailed policy in SPEC-13
```

SQLite WAL/SHM companion files are implementation-owned siblings of the corresponding DB and inherit the same security boundary.

Examples while a DB is active:

```text
machine.db
machine.db-wal
machine.db-shm
```

The application MUST NOT treat only the main `.db` file as the entire live database state.

---

## 3. Known folder resolution

Do not construct paths from literal `C:\ProgramData` or `C:\Users\<name>`.

Resolve:

```text
FOLDERID_ProgramData
FOLDERID_LocalAppData
```

and append SplitOS-owned relative paths.

This preserves Windows folder redirection/configuration behavior.

---

## 4. Machine data ACL baseline

`%ProgramData%\SplitOS\Data` is a protected machine-state boundary.

Baseline principals:

```text
SYSTEM                     full control
Administrators             administrative/recovery control
NT SERVICE\SplitOSBroker   access required by Broker service policy
ordinary Users             no direct write access
```

The exact installed SDDL is finalized with Broker hardening tests, but the following semantic rule is normative:

```text
ordinary interactive user process
!= direct machine.db writer
```

Runtime Host reaches machine persistence through typed Broker capabilities.

If a recovery/support tool is later granted direct access, it must be a signed/privileged SplitOS component with explicit specification coverage.

---

## 5. Machine backups ACL

`%ProgramData%\SplitOS\Backups\machine` uses at least the same protection level as `machine.db`.

A backup containing canonical machine state MUST NOT become a less protected copy of the database.

Backup filenames SHOULD carry non-authoritative metadata only, e.g.:

```text
machine-schema-0003-20260904T170000Z.db
```

The filename does not prove backup validity; integrity/schema checks do.

---

## 6. User data ACL baseline

`%LocalAppData%\SplitOS\Data` follows the current Windows user's private local application-data boundary.

Normal writer:

```text
SplitOS.RuntimeHost.exe under current Windows user token
```

Manager/Game Launcher do not open `user.db` directly.

Another ordinary Windows user gets a different `%LocalAppData%` tree and MUST NOT be granted explicit access to this database.

---

## 7. Projection cache boundary

`%LocalAppData%\SplitOS\Cache\projection.db` is per-user and rebuildable.

Cache ACL need not be stronger than `user.db`, but cache content MUST still be treated as untrusted/revalidated evidence because external clients/files can change independently.

A cache file is never a security authority simply because it resides under the user's profile.

---

## 8. Protected secret boundary

Actual reusable authentication credentials are not persisted in `user.db`.

Conceptual layout:

```text
user.db
└── secret reference / account association metadata

IProtectedSecretStore
└── protected credential material
```

`IProtectedSecretStore` backing mechanism is finalized in `SPEC-04`; Trust baseline currently favors Windows user-scoped protection such as DPAPI.

No database backup process should accidentally export reusable plaintext credentials.

---

## 9. Release knowledge and immutable artifacts

Signed manifests/component catalogs belong to the installed release/artifact boundary, not mutable Data folders.

A DB may reference:

```text
releaseId
manifestId
manifestDigest
schema compatibility version
```

but it does not replace the signed release artifact.

---

## 10. Staging separation

Update/recovery staging lives under the machine root but outside `Data`:

```text
%ProgramData%\SplitOS\Staging
```

Reason:

```text
verified/staged release artifact
!= canonical state database
```

A cleanup of staging MUST NOT delete machine state, and DB recovery MUST NOT trust a staged package merely because it exists there.

---

## 11. Database open rules

### `machine.db`

Opened by Broker persistence implementation.

Runtime Host does not obtain a raw DB path/connection for normal operation.

### `user.db`

Opened by the per-session Runtime Host.

### `projection.db`

Opened by the per-session Runtime Host; failure may trigger discard/rebuild.

Manager/Game Launcher use semantic Runtime IPC.

---

## 12. One writer architecture

Although SQLite supports multiple connections, SplitOS intentionally keeps write authority narrow:

```text
machine.db     → Broker persistence gateway
user.db        → Runtime Host user persistence gateway
projection.db  → Runtime Host projection gateway
```

This reduces accidental cross-owner writes and makes migrations/shutdown/checkpoint behavior predictable.

Read connections MAY be pooled internally within the owning process if provider semantics are safe.

---

## 13. WAL file lifecycle

WAL mode means the database can have live state in `-wal` and `-shm` files.

Rules:

1. do not manually copy only `*.db` while the database is live;
2. use SQLite backup/checkpoint-aware APIs for backups;
3. never delete `-wal`/`-shm` as a routine corruption fix while a database connection may be active;
4. quarantine/recovery operates after owning process closes or through SQLite-supported procedures;
5. ACLs for companion files must not widen access relative to the main database.

---

## 14. Disk locality

v1 databases are local-machine stores.

They MUST NOT be placed intentionally on SMB/network shares or cloud-sync redirected roots as a supported configuration.

This is especially important for WAL coordination and for machine-state reliability.

If enterprise folder redirection maps a user location in an unsupported way, Runtime Host must detect/report an unsupported persistence environment rather than silently claim full guarantees.

---

## 15. Space pressure

Persistence failures caused by disk-full conditions are real failures.

Canonical rule:

```text
required durable write failed
→ semantic commit cannot be reported durable
```

Projection cache may be evicted under pressure.

Canonical user/machine DBs MUST NOT be deleted automatically to recover space.

---

## 16. Result

Physical placement is therefore:

```text
Machine canonical / transactions
→ ProgramData
→ Broker-mediated
→ protected ACL

User canonical
→ LocalAppData
→ RuntimeHost-mediated
→ user ACL

Rebuildable projections
→ LocalAppData Cache
→ RuntimeHost-mediated
→ discard/rebuild allowed

Secrets
→ separate protected-secret abstraction
```
