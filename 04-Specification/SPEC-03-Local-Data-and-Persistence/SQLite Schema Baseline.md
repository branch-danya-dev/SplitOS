# SPEC-03 — SQLite Schema Baseline

## 1. Purpose

Defines the initial v1 physical schema shape for SplitOS local databases.

This schema is intentionally sufficient for persistence contracts while leaving domain-specific fields to later specifications where they belong.

Rules:

```text
schema structure
!= semantic ownership

JSON extension payload
!= permission to bypass validation
```

---

# Part A — Common SQLite rules

## 2. Common conventions

### Identifiers

Logical IDs are stored as opaque `TEXT` values compatible with UUID-style identifiers.

No business meaning may be inferred from ID formatting.

### Timestamps

UTC timestamps are stored as ISO-8601 text in canonical form:

```text
YYYY-MM-DDTHH:MM:SS.fffZ
```

Provider code MUST normalize before write.

### Booleans

Stored as INTEGER with `CHECK(value IN (0,1))` where material.

### Revisions

Mutable canonical records use:

```text
revision INTEGER NOT NULL CHECK(revision >= 1)
```

### JSON payloads

JSON is permitted only for bounded/versioned extension payloads whose semantic owner validates the schema.

No table may use a generic JSON blob as a substitute for all domain fields.

### Foreign keys

All canonical databases use:

```sql
PRAGMA foreign_keys = ON;
```

Schema changes MUST include foreign-key validation in migration verification.

---

## 3. Metadata table

Each DB contains:

```sql
CREATE TABLE schema_metadata (
    component_key          TEXT PRIMARY KEY,
    schema_version         INTEGER NOT NULL CHECK(schema_version >= 1),
    created_utc            TEXT NOT NULL,
    last_migrated_utc      TEXT NOT NULL,
    release_id             TEXT NULL
);
```

SQLite `PRAGMA user_version` MUST also match the database-level physical schema version expected by the current release.

Mismatch between `user_version` and SplitOS metadata is a migration/integrity failure, not something to ignore.

---

# Part B — machine.db

## 4. Installation identity

Exactly one active installation identity is expected per machine DB.

```sql
CREATE TABLE splitos_installation (
    singleton_id           INTEGER PRIMARY KEY CHECK(singleton_id = 1),
    installation_id        TEXT NOT NULL UNIQUE,
    created_utc            TEXT NOT NULL,
    current_release_id     TEXT NOT NULL,
    revision               INTEGER NOT NULL CHECK(revision >= 1),
    updated_utc            TEXT NOT NULL,
    updated_by_operation_id TEXT NULL
);
```

`current_release_id` is local installed relationship metadata. It does not replace signed release provenance.

---

## 5. Installed baseline identity

```sql
CREATE TABLE installed_baseline (
    singleton_id            INTEGER PRIMARY KEY CHECK(singleton_id = 1),
    baseline_id             TEXT NOT NULL,
    release_id              TEXT NOT NULL,
    manifest_id             TEXT NOT NULL,
    manifest_digest         TEXT NOT NULL,
    verified_utc            TEXT NOT NULL,
    verified_by_operation_id TEXT NOT NULL,
    revision                INTEGER NOT NULL CHECK(revision >= 1),
    updated_utc             TEXT NOT NULL
);
```

A row may be replaced/updated only after the owning build/update lifecycle proves the target baseline.

---

## 6. Operational mode state

```sql
CREATE TABLE operational_mode_state (
    singleton_id             INTEGER PRIMARY KEY CHECK(singleton_id = 1),
    committed_mode           TEXT NOT NULL
                               CHECK(committed_mode IN ('NONE','WORK','GAME')),
    committed_utc            TEXT NOT NULL,
    committed_by_operation_id TEXT NOT NULL,
    correlation_id           TEXT NULL,
    revision                 INTEGER NOT NULL CHECK(revision >= 1),
    updated_utc              TEXT NOT NULL
);
```

Interpretation:

```text
NONE
→ valid for FREE / no managed operational mode

WORK/GAME
→ only valid according to Runtime Access + Mode State semantics
```

The table does not assert that actual Windows settings currently match the committed mode.

---

## 7. Mode transition journal

The physical schema supports the A&D transition lifecycle while leaving exact stage detail to `SPEC-05`.

```sql
CREATE TABLE mode_transition (
    transition_id            TEXT PRIMARY KEY,
    correlation_id           TEXT NOT NULL,
    source_mode              TEXT NOT NULL
                               CHECK(source_mode IN ('NONE','WORK','GAME')),
    target_mode              TEXT NOT NULL
                               CHECK(target_mode IN ('WORK','GAME')),
    transition_state         TEXT NOT NULL,
    stage_code               TEXT NOT NULL,
    started_utc              TEXT NOT NULL,
    updated_utc              TEXT NOT NULL,
    commit_durable           INTEGER NOT NULL DEFAULT 0
                               CHECK(commit_durable IN (0,1)),
    terminal_outcome         TEXT NULL,
    recovery_context_id      TEXT NULL,
    revision                 INTEGER NOT NULL CHECK(revision >= 1)
);

CREATE INDEX ix_mode_transition_state
    ON mode_transition(transition_state, updated_utc);
```

`transition_state`, `stage_code` and `terminal_outcome` values are restricted by repository-level domain validation defined in `SPEC-05`.

Only one active machine-wide mode transition is permitted by semantic orchestration; the DB is not the sole locking mechanism for that invariant.

---

## 8. Update transaction journal

```sql
CREATE TABLE update_transaction (
    update_transaction_id    TEXT PRIMARY KEY,
    correlation_id           TEXT NOT NULL,
    source_release_id        TEXT NOT NULL,
    target_release_id        TEXT NOT NULL,
    transaction_state        TEXT NOT NULL,
    stage_code               TEXT NOT NULL,
    started_utc              TEXT NOT NULL,
    updated_utc              TEXT NOT NULL,
    reboot_required          INTEGER NOT NULL DEFAULT 0
                               CHECK(reboot_required IN (0,1)),
    reboot_requested_utc     TEXT NULL,
    verified_target          INTEGER NOT NULL DEFAULT 0
                               CHECK(verified_target IN (0,1)),
    recovery_context_id      TEXT NULL,
    revision                 INTEGER NOT NULL CHECK(revision >= 1)
);

CREATE INDEX ix_update_transaction_state
    ON update_transaction(transaction_state, updated_utc);
```

Exact resumable stage semantics belong to `SPEC-11`.

---

## 9. Recovery context

```sql
CREATE TABLE recovery_context (
    recovery_context_id      TEXT PRIMARY KEY,
    correlation_id           TEXT NOT NULL,
    source_operation_type    TEXT NOT NULL,
    source_operation_id      TEXT NOT NULL,
    recovery_state           TEXT NOT NULL,
    selected_strategy_id     TEXT NULL,
    attempt_number           INTEGER NOT NULL DEFAULT 0 CHECK(attempt_number >= 0),
    safe_target_code         TEXT NULL,
    started_utc              TEXT NOT NULL,
    updated_utc              TEXT NOT NULL,
    verified_result          INTEGER NOT NULL DEFAULT 0
                               CHECK(verified_result IN (0,1)),
    revision                 INTEGER NOT NULL CHECK(revision >= 1)
);

CREATE INDEX ix_recovery_context_state
    ON recovery_context(recovery_state, updated_utc);
```

---

## 10. Machine idempotency ledger

To support Broker-mediated durable mutations where replay safety matters:

```sql
CREATE TABLE machine_idempotency (
    idempotency_key          TEXT PRIMARY KEY,
    capability_id            TEXT NOT NULL,
    request_digest           TEXT NOT NULL,
    operation_id             TEXT NOT NULL,
    first_seen_utc           TEXT NOT NULL,
    result_code              TEXT NULL,
    completed_utc            TEXT NULL,
    result_reference         TEXT NULL
);

CREATE INDEX ix_machine_idempotency_operation
    ON machine_idempotency(operation_id);
```

This table stores no arbitrary raw request secrets.

Retention policy must preserve keys long enough to cover legitimate retry windows; exact pruning belongs to implementation/operation policy.

---

# Part C — user.db

## 11. Windows user ↔ SplitOS account association

```sql
CREATE TABLE account_association (
    singleton_id             INTEGER PRIMARY KEY CHECK(singleton_id = 1),
    windows_user_sid         TEXT NOT NULL,
    splitos_account_id       TEXT NULL,
    association_state        TEXT NOT NULL,
    associated_utc           TEXT NULL,
    last_validated_utc       TEXT NULL,
    secret_reference         TEXT NULL,
    revision                 INTEGER NOT NULL CHECK(revision >= 1),
    updated_utc              TEXT NOT NULL
);
```

`secret_reference` is an opaque handle/reference only. It MUST NOT contain the reusable token itself.

Account identity/entitlement truth remains backend-owned.

---

## 12. Games referenced by user configuration

A lightweight SplitOS game identity table supports stable profile references independent of current client installation evidence.

```sql
CREATE TABLE game_identity (
    game_id                  TEXT PRIMARY KEY,
    canonical_name           TEXT NOT NULL,
    created_utc              TEXT NOT NULL,
    updated_utc              TEXT NOT NULL,
    revision                 INTEGER NOT NULL CHECK(revision >= 1)
);
```

This table does not prove installation/license state.

---

## 13. Game Profile

```sql
CREATE TABLE game_profile (
    profile_id               TEXT PRIMARY KEY,
    game_id                  TEXT NOT NULL,
    profile_name             TEXT NOT NULL,
    is_default               INTEGER NOT NULL DEFAULT 0
                               CHECK(is_default IN (0,1)),
    profile_schema_version   INTEGER NOT NULL CHECK(profile_schema_version >= 1),
    display_selector_json    TEXT NULL,
    input_selector_json      TEXT NULL,
    optimization_json        TEXT NULL,
    game_config_json         TEXT NULL,
    created_utc              TEXT NOT NULL,
    updated_utc              TEXT NOT NULL,
    revision                 INTEGER NOT NULL CHECK(revision >= 1),
    FOREIGN KEY(game_id) REFERENCES game_identity(game_id)
        ON DELETE CASCADE
);

CREATE INDEX ix_game_profile_game
    ON game_profile(game_id, is_default, profile_name);
```

JSON columns are versioned domain payloads and MUST be validated by Game Profile/Optimization owners before persistence.

`SPEC-08` may normalize fields into additional columns/tables if query or integrity requirements justify it.

---

## 14. User preferences

```sql
CREATE TABLE user_preference (
    preference_key           TEXT PRIMARY KEY,
    owning_module            TEXT NOT NULL,
    value_schema_version     INTEGER NOT NULL CHECK(value_schema_version >= 1),
    value_json               TEXT NOT NULL,
    updated_utc              TEXT NOT NULL,
    revision                 INTEGER NOT NULL CHECK(revision >= 1)
);
```

Allowed `preference_key` and owning module mappings are release/product-defined.

No consumer may invent an arbitrary key and thereby create a hidden global configuration namespace.

---

## 15. Shared App preferences

```sql
CREATE TABLE shared_app_preference (
    application_id           TEXT PRIMARY KEY,
    presentation_mode        TEXT NOT NULL
                               CHECK(presentation_mode IN (
                                   'OVERLAY',
                                   'LOCKED_WINDOW',
                                   'SECONDARY_DISPLAY',
                                   'BACKGROUND'
                               )),
    enabled                  INTEGER NOT NULL DEFAULT 1
                               CHECK(enabled IN (0,1)),
    display_selector_json    TEXT NULL,
    updated_utc              TEXT NOT NULL,
    revision                 INTEGER NOT NULL CHECK(revision >= 1)
);
```

The v1 maximum-active Shared Apps policy is a behavior/UX rule, not enforced only by row count.

---

# Part D — projection.db

## 16. Game Client projection

```sql
CREATE TABLE game_client_projection (
    client_id                TEXT PRIMARY KEY,
    client_type              TEXT NOT NULL,
    availability_state       TEXT NOT NULL,
    capability_status_json   TEXT NOT NULL,
    observed_at_utc          TEXT NOT NULL,
    expires_at_utc           TEXT NULL,
    projection_schema_version INTEGER NOT NULL CHECK(projection_schema_version >= 1)
);
```

---

## 17. Game installation projection

```sql
CREATE TABLE game_installation_projection (
    projection_id            TEXT PRIMARY KEY,
    game_id                  TEXT NOT NULL,
    client_id                TEXT NOT NULL,
    external_game_id         TEXT NOT NULL,
    install_state            TEXT NOT NULL,
    validated_install_path   TEXT NULL,
    launch_reference         TEXT NULL,
    observed_at_utc          TEXT NOT NULL,
    expires_at_utc           TEXT NULL,
    confidence_code          TEXT NULL,
    capability_status        TEXT NOT NULL,
    projection_schema_version INTEGER NOT NULL CHECK(projection_schema_version >= 1),
    UNIQUE(client_id, external_game_id),
    FOREIGN KEY(client_id) REFERENCES game_client_projection(client_id)
        ON DELETE CASCADE
);

CREATE INDEX ix_game_install_projection_game
    ON game_installation_projection(game_id, install_state);
```

`validated_install_path` is evidence only and MUST be revalidated before security-sensitive use.

---

## 18. Optional device projection cache

Short-lived device snapshots MAY be cached physically if profiling/performance benefits justify it:

```sql
CREATE TABLE device_projection (
    projection_id            TEXT PRIMARY KEY,
    device_class             TEXT NOT NULL,
    stable_identity_hint     TEXT NULL,
    payload_json             TEXT NOT NULL,
    observed_at_utc          TEXT NOT NULL,
    expires_at_utc           TEXT NOT NULL,
    projection_schema_version INTEGER NOT NULL CHECK(projection_schema_version >= 1)
);
```

Expired device projections MUST NOT satisfy a required fresh hardware check.

---

# Part E — indexes, queries, constraints

## 19. Query principle

Indexes exist for known product access paths, not speculative analytics.

Initial important access paths:

```text
active/incomplete transaction by state
profiles by game
projection by game/client
recovery/update records by state/time
```

Additional indexes require evidence from actual query/latency behavior.

---

## 20. No database triggers for semantic state machines

SQLite triggers MUST NOT implement Work/Game transition semantics, entitlement decisions, update state machines or recovery policy.

Reason:

```text
DB trigger
→ hidden semantic owner
```

Triggers may be used only for narrowly technical integrity behavior if documented and reviewed.

---

## 21. No dynamic SQL from IPC/UI

Database gateways MUST use parameterized/compiled repository operations.

Raw SQL strings MUST NOT cross:

- Manager → Runtime Host IPC;
- Game Launcher → Runtime Host IPC;
- Runtime Host → Broker IPC.

---

## 22. Schema evolution rule

Later specs may add fields/tables but MUST preserve:

- machine/user/projection separation;
- semantic ownership;
- revision/operation identity where durability depends on it;
- projection provenance/freshness;
- no reusable plaintext credential storage;
- no silent downgrade of schema meaning.

Breaking physical changes require explicit migration version increase.

---

## 23. Result

The schema baseline gives implementation concrete persistence targets while deliberately leaving domain semantics with their owning specifications.
