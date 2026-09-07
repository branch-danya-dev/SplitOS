# SPEC-14 — Build, Install, Update & Recovery Acceptance

## 1. Purpose

Defines acceptance evidence for the full lifecycle:

```text
Windows source
→ SplitOS build
→ clean install
→ first run
→ supported operation
→ SplitOS update
→ reboot/resume
→ rollback/recovery
```

The lifecycle is accepted only if each authority boundary and transition can be proven against exact release artifacts.

---

# 2. Build acceptance

## 2.1 Source identity

Mandatory assertions:

```text
source exists
source identity can be derived
edition/build/arch/index match ReleaseAcceptanceProfile
source integrity/provenance evidence meets release policy
unsupported source is rejected
```

Builder must not “best effort” an unknown Windows source into an official baseline.

---

## 2.2 Manifest acceptance

Verify production BuildManifest:

- schema validates strictly;
- manifest version supported;
- every operation is from typed allowlist;
- no arbitrary PowerShell/command/path/registry primitive exists;
- referenced component IDs exist in accepted Component Matrix;
- package/artifact identities match trusted release definition;
- operation ordering/dependencies are valid.

Malformed/unknown operation fails build before unsafe mutation.

---

## 2.3 Component Matrix acceptance

For every production classification:

```text
componentId
lifecycleClass
mechanism
validation status
Windows Base applicability
dependency notes
postconditions
```

must be resolvable.

A production build must not consume `TBD/HYPOTHESIS` destructive action as accepted `REMOVE`.

---

## 2.4 Offline servicing verification

For each operation:

```text
invoke supported mechanism
→ capture technical result
→ inspect offline image actual state
→ evaluate declared postcondition
```

Command exit code alone is insufficient.

---

## 2.5 BuildReceipt

A successful build produces receipt containing exact:

```text
Builder version
ADK/DISM/toolchain identity
source identity
image index
BuildManifest digest
Component Matrix digest
SplitOS package digests
operation results
verification results
output artifact identity/hash
```

Receipt mismatch blocks GATE-00/GATE-01.

---

# 3. Prepared baseline boot/install acceptance

Minimum system tests on supported matrix cells:

```text
boot installation environment/media path
Windows Setup completion
OOBE completion
local/Microsoft Windows account paths as supported by Windows baseline
Windows user creation
first interactive sign-in
RuntimeHost startup
Broker service installed/running
Manager availability
SplitOS First Run
Windows Desktop usable
```

No SplitOS-first-run failure may make Windows sign-in impossible.

---

# 4. Installed component-state acceptance

After clean install, compare actual machine state to release expectations:

```text
KEEP → required component present/functional
REMOVE → accepted absent/deprovisioned state verified
DISABLE → present but inactive baseline where specified
MODE_MANAGED → present with BASE state ready for runtime management
```

Do not infer component success only from build-time image inspection; installed-state checks are required for selected critical components.

---

# 5. Servicing substrate acceptance

Because SplitOS is still Windows-based, verify required Windows mechanisms remain functional:

```text
component servicing health
Windows Update servicing path presence according to policy
WinRE presence/registered state
Store/app deployment substrate where required
Gaming Services dependencies where required
networking/auth/device infrastructure
```

Aggressive component removal cannot pass if it breaks declared supported servicing/recovery scenarios.

---

# 6. SplitOS update discovery acceptance

For candidate update N→N+1:

Verify:

- current release/source edge is allowed;
- Windows compatibility decision allows target;
- TUF metadata chain valid/current;
- exact Release Envelope selected;
- entitlement/update capability rules satisfied where applicable;
- target not rejected by anti-rollback/security floors.

No mutation yet.

---

# 7. Artifact download / staging acceptance

Before activation:

```text
all required artifacts downloaded
all target lengths/hashes match trusted metadata
PE artifacts pass allowed Authenticode publisher validation
staging ACL/boundary valid
staged release internally complete
```

Interrupted download may resume/retry but cannot produce `STAGED_VERIFIED` without final verification.

---

# 8. Recovery Capsule precondition

Mandatory for production update where SPEC-11 requires previous-release local rollback target.

Before N+1 activation:

```text
capture authenticated N release material
capture required machine-state/recovery metadata
create isolated capsule
verify capsule integrity/readability
verify exact recovery authorization availability/policy
seal capsule
```

Expected state:

```text
SEALED_VERIFIED
```

Anything less blocks activation.

---

# 9. Update mutation acceptance

The update owns the major mutation lease.

Verify:

- Mode transition cannot concurrently own machine mutation;
- Recovery cannot blindly interleave;
- fence token propagates to privileged operations;
- target release is activated through trusted typed bootstrap path;
- Broker self-replacement works without exposing generic arbitrary updater execution;
- durable UpdateTransaction advances correctly.

---

# 10. Reboot/resume acceptance

For updates requiring reboot:

```text
pre-reboot durable transaction state
→ Windows reboot
→ target/source startup path
→ locate active incomplete transaction
→ resume/reconcile
→ actual-state verification
```

Must distinguish:

```text
expected reboot
unexpected reboot/power loss
already committed target
not-yet-committed target
ambiguous/corrupt transaction
```

Ambiguity escalates to recovery; never guess target release.

---

# 11. Target health acceptance

Before `InstalledSplitOSRelease` changes to N+1, verify at least:

```text
expected target artifacts active
Broker starts and trusted IPC works
RuntimeHost starts
Runtime ↔ Broker protocol compatible
machine/user DB schema/migrations valid
release-owned catalogs load
Windows Base compatibility still valid
critical Windows context adapters initialize
required security floors updated consistently
```

Additional release-specific health checks may be declared.

---

# 12. Commit acceptance

The installed release identity commit is atomic/durable relative to the update transaction commit marker.

After commit, test immediate process crash/reboot and verify target remains canonical.

---

# 13. User data migration acceptance

For N→N+1:

Use fixture user data containing:

```text
multiple Game Profiles
field-level overrides
TV/Desktop profiles
Shared App assignments
account association metadata
preferences
historically added/unknown forward-compatible fields where applicable
```

Verify migration preserves intended semantic data.

No migration may pass by replacing user DB with empty/default database.

---

# 14. Rollback compatibility acceptance

For mandatory previous-release rollback:

After running N+1 and creating/modifying user data that N policy says must survive:

```text
recover software N+1 → N
```

Verify:

- current user data remains preserved;
- N can read/preserve forward-compatible data according to contract, or rollback bridge performs valid transform;
- no old `%UserProfile%`/old user DB snapshot replaces current user data;
- account/entitlement behavior remains coherent;
- runtime boots to safe valid state.

---

# 15. Recovery authorization acceptance

Use exact fixtures:

```text
valid N+1 → N recovery authorization
wrong source
wrong target
expired/revoked authorization where applicable
old authentic release without authorization
```

Only the exact currently authorized recovery path may pass.

---

# 16. WinRE recovery acceptance

On selected physical/support matrix cells:

```text
break normal SplitOS runtime sufficiently to require offline recovery
→ enter Windows RE
→ launch bounded SplitOS Recovery Tool
→ inspect transaction/capsule
→ restore authorized release
→ verify
→ reboot Windows
→ verify Windows usable + SplitOS safe state
```

Recovery Tool must not expose arbitrary shell/PowerShell/path editing surface under SplitOS product contract.

---

# 17. Windows-level failure boundary

Create scenario where failure is genuinely Windows-level (boot/component store/driver class as safely reproducible in lab).

Expected:

```text
SplitOS Recovery identifies inability to repair Windows platform
→ does not claim successful SplitOS rollback as full machine repair
→ routes to Windows-native recovery path
```

After Windows recovery, SplitOS baseline is revalidated/repaired as required.

---

# 18. Update vs Windows servicing coexistence

Verify SplitOS does not start major wrapper mutation while incompatible/pending Windows servicing state makes it unsafe according to SPEC-11 policy.

Also verify SplitOS feed does not repackage arbitrary Microsoft Windows patch payload as SplitOS-owned artifact authority.

---

# 19. Space-pressure acceptance

Test free-space boundaries declared by release.

Expected:

```text
insufficient space for stage + capsule + safety reserve
→ update blocked before mutation
```

Updater must not delete required current Recovery Capsule or canonical user data to manufacture free space.

---

# 20. Cleanup acceptance

After successful update:

- staging cleanup does not delete canonical state;
- one required previous-release capsule remains according to policy;
- superseded capsule removal occurs only after replacement is verified when rotating recovery target;
- logs/diagnostics cleanup respects retention priority;
- security floors remain durable outside rebuildable cache.

---

# 21. Build/update reproducibility evidence

Release evidence must be sufficient to answer:

```text
which Windows source?
which manifest/matrix?
which packages?
which final signed hashes?
which update source edge?
which capsule?
which recovery authorization?
which tests?
```

---

# 22. Blocking conditions

Examples of release blockers:

```text
unsupported source accepted
mandatory build postcondition not verified
clean install/OOBE broken
Recovery Capsule creation can be bypassed
update commits before target health verification
reboot loses transaction identity
software rollback restores old user data snapshot
unauthorized old release accepted
WinRE recovery path absent where declared required
Windows platform rendered unusable by accepted component matrix without supported recovery
```

---

# 23. Result

The build/install/update/recovery lifecycle passes only when SplitOS can prove both creation of a known baseline and safe evolution back and forth across the release edges it publicly supports.
