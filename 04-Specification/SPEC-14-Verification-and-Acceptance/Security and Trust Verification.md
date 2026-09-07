# SPEC-14 — Security and Trust Verification

## 1. Purpose

Defines release-blocking verification for SplitOS trust boundaries, privileged local control and release supply-chain security.

Security verification proves denial behavior as well as allowed behavior.

Core rule:

```text
signed / local / SplitOS-branded
!= automatically trusted
```

---

# 2. Security gate structure

Mandatory groups:

```text
SEC-A  Local process / IPC trust
SEC-B  Privileged Broker capability boundary
SEC-C  Account / entitlement trust
SEC-D  External evidence containment
SEC-E  Release repository / TUF trust
SEC-F  Authenticode / artifact trust
SEC-G  Anti-rollback / recovery authorization
SEC-H  Secrets / diagnostics / privacy
SEC-I  Key rotation / revocation fixtures
```

Failures in mandatory in-scope cases are release blocking.

---

# 3. Local process / IPC trust

## SEC-A-001 — Wrong process on expected pipe

Connect from an ordinary user process to Broker pipe.

Expected: caller validation denies capability before mutation.

## SEC-A-002 — Copied/renamed executable

Run a binary with expected filename but wrong path/publisher/release provenance.

Expected: filename alone is insufficient; connection/mutation denied where provenance is required.

## SEC-A-003 — Wrong Windows session

Signed Runtime Host from non-control session requests machine mutation.

Expected: denied.

## SEC-A-004 — Remote Named Pipe client

Attempt remote pipe connection.

Expected: transport/security configuration rejects remote use; no privileged operation.

## SEC-A-005 — Protocol version mismatch

Unsupported major IPC protocol version.

Expected explicit incompatibility; no fallback to unversioned permissive protocol.

---

# 4. Broker capability boundary

## SEC-B-001 — Unknown capability

Send arbitrary capability string.

Expected `UNKNOWN_CAPABILITY`/equivalent deny.

## SEC-B-002 — Arbitrary command request

Attempt payload representing command line/PowerShell/arbitrary executable.

Expected no production capability maps to it.

## SEC-B-003 — Arbitrary registry path

Try to convert typed policy operation into caller-controlled registry path/value.

Expected Broker resolves release-owned `ManagedPolicyId`; arbitrary path rejected.

## SEC-B-004 — Arbitrary service name

Caller provides service name outside release-owned catalog.

Expected denied.

## SEC-B-005 — Capability/payload mismatch

Valid capability ID with malformed or semantically incompatible payload.

Expected schema/semantic reject before privileged operation.

## SEC-B-006 — Replayed idempotency key with different payload

Expected `IDEMPOTENCY_CONFLICT`; no second mutation.

## SEC-B-007 — Stale fence

Expected deny and security audit.

---

# 5. Account / entitlement trust

## SEC-C-001 — Forged browser callback

Callback contains invented success parameters without valid backend state.

Expected no PRO grant.

## SEC-C-002 — Tampered offline assertion

Modify entitlement capability/account/device/expiry claims without valid signature.

Expected reject.

## SEC-C-003 — Assertion replay across account/installation

Use valid assertion from a different bound account/installation/association.

Expected reject.

## SEC-C-004 — Expired assertion

Expected premium capability unavailable after policy-defined validity.

## SEC-C-005 — Clock rollback

Expected no validity extension from local clock manipulation.

## SEC-C-006 — Secret-store file copied to another Windows user/context

Expected protected credential is not reusable as plaintext equivalent in unrelated user context.

---

# 6. External evidence containment

## SEC-D-001 — Malicious Game Client metadata path

Inject path traversal/unexpected executable/command-like content into version-sensitive local metadata fixture.

Expected parser treats it as untrusted evidence and does not produce privileged execution input.

## SEC-D-002 — Malformed manifest

Fuzz supported local Game Client metadata parsers.

Expected local adapter failure/degraded projection, not Runtime/Broker compromise or arbitrary command execution.

## SEC-D-003 — External client reports install/license change

Verify projection reconciles without external evidence directly writing SplitOS canonical product entitlement/mode/release state.

## SEC-D-004 — Process spoofing

Create process with game-like name/path ambiguity but insufficient proof set.

Expected weak evidence alone cannot establish `GAME_RUNNING`.

---

# 7. TUF repository trust

## SEC-E-001 — Valid current metadata accepted

Verify embedded root→timestamp→snapshot→targets/delegation chain for exact candidate.

## SEC-E-002 — Tampered targets metadata

Expected signature/hash verification failure.

## SEC-E-003 — Expired timestamp

Expected new release activation blocked; current installed release remains usable.

## SEC-E-004 — Freeze attack fixture

Serve old but correctly signed timestamp/snapshot below locally trusted metadata/version/time floors.

Expected reject/stale detection.

## SEC-E-005 — Metadata rollback

Serve lower metadata version after client has trusted higher version.

Expected reject.

## SEC-E-006 — Mix-and-match metadata

Combine valid metadata from inconsistent versions.

Expected snapshot/hash/version constraints reject.

## SEC-E-007 — Delegation path violation

Use knowledge role to authorize release binary path or release role to authorize unauthorized recovery path.

Expected reject.

## SEC-E-008 — CDN compromise model

Serve arbitrary bytes/metadata from otherwise valid HTTPS endpoint.

Expected transport location alone grants no trust.

---

# 8. Authenticode / artifact trust

## SEC-F-001 — TUF-valid hash but disallowed publisher

Fixture release metadata references exact artifact signed by non-allowed publisher.

Expected executable activation/install reject.

## SEC-F-002 — Allowed publisher signature but artifact absent from trusted release metadata

Expected reject as unauthorized release artifact.

## SEC-F-003 — Artifact modified after signing

Expected Authenticode/hash validation failure.

## SEC-F-004 — Artifact modified after release metadata hash binding

Expected target digest mismatch.

## SEC-F-005 — Validly signed unexpected old publisher identity

Use publisher revoked/deauthorized by trusted policy.

Expected reject according to current trusted publisher policy.

## SEC-F-006 — Release metadata hash computed before signing fixture

Verify pipeline acceptance rejects mismatch between signed final artifact and metadata hash.

---

# 9. Anti-rollback / recovery authorization

## SEC-G-001 — Old authentic release normal downgrade

Expected denied by release sequence/security epoch/floor policy.

## SEC-G-002 — Recovery authorization wrong source

Authorization `N+1→N` presented while current source is different release.

Expected reject.

## SEC-G-003 — Recovery authorization wrong target

Expected reject.

## SEC-G-004 — Recovery authorization wrong context

Use `RECOVERY_ONLY` authorization as normal updater downgrade.

Expected reject.

## SEC-G-005 — Recovery authorization below current security floor

Expected reject unless explicit security policy says the exact recovery is still allowed and signed accordingly; no implicit bypass.

## SEC-G-006 — Manual user intent cannot override anti-rollback

Manager “restore previous” action without valid authorization fails safely.

---

# 10. Root/key rotation fixtures

Security qualification must include offline fixture repositories demonstrating:

```text
valid delegated key rotation
revoked release key
revoked recovery key
publisher key replacement
sequential root update
old-root/new-root threshold transition
```

## SEC-I-001 — Root update missing old threshold

Expected reject.

## SEC-I-002 — Root update missing new threshold

Expected reject.

## SEC-I-003 — Skipped root version where sequential policy requires intermediate

Expected reject/request required intermediate metadata.

## SEC-I-004 — Revoked release metadata key

New metadata signed solely by revoked key is rejected.

## SEC-I-005 — Catastrophic root-threshold compromise simulation

Verify ordinary self-update logic has no “trust any new root from backend” escape hatch.

This scenario documents out-of-band recovery requirement; production client must fail closed rather than invent trust.

---

# 11. Signing pipeline verification

Release qualification of signing pipeline includes:

- ordinary CI cannot export/read raw production private key;
- signer authorizes only expected artifact class/capability;
- signed result is re-verified before release metadata generation;
- final digest is computed after signing/timestamping;
- production metadata role separation enforced;
- root/targets/recovery ceremonies require configured threshold;
- audit/evidence identifies signing request/approval without exposing key material.

A test or staging key does not prove production key custody controls.

---

# 12. Diagnostic/secret trust

## SEC-H-001 — Secret field rejected by event API

Attempt to emit known token/Authorization header fixture into normal event schema.

Expected field rejected/redacted according to API boundary.

## SEC-H-002 — Bundle redaction validation

Seed diagnostic source with synthetic secret patterns.

Expected no forbidden secret in successful export.

## SEC-H-003 — Redactor failure

Force redaction validator error.

Expected export blocked.

## SEC-H-004 — Full dump explicit only

Verify default crash policy uses minidump; full process memory capture requires explicit deep-diagnostics action/warning and is not automatically uploaded.

---

# 13. Privilege regression tests

Every production release that changes Broker/installer/service manifest/ACL must re-run at least:

```text
ordinary user write denial to machine state
cross-session mutation denial
UI→Broker direct denial
remote pipe denial
unknown capability denial
arbitrary command/path/service denial
stale fence denial
expected allowed capability success
security audit emitted
```

Because a single ACL/service configuration change can invalidate prior boundary evidence.

---

# 14. Fuzz / robustness

Mandatory fuzz targets should include where implementation exists:

```text
IPC frame length / JSON envelope
Broker capability payload schemas
Game Client local metadata parsers
Release Envelope / security metadata parser
Diagnostic event payload/redaction pipeline
Build Manifest parser
Game config parser/adapters
```

Fuzz success criteria are not only “no crash”:

```text
no arbitrary execution
no privilege escalation
no canonical state mutation from invalid input
bounded resource consumption where practical
clean error classification
```

---

# 15. Security evidence package

GATE-08 evidence includes:

```text
security case execution results
exact production-equivalent trust metadata fixture versions
publisher certificate identities/policy version
Broker ACL/service configuration identity
negative-case audit records
fuzz campaign summary where required
known accepted threat-model exclusions
security owner sign-off
```

Do not include private key material.

---

# 16. Non-goals / threat boundary

Acceptance does not claim protection from already-declared out-of-scope adversaries such as unrestricted malicious local Administrator/kernel/firmware/hypervisor compromise.

But ordinary-user/process, cross-session, malformed-input, stale/replayed request and supply-chain attack classes inside the v1 trust model are in scope and must be tested.

---

# 17. Result

A SplitOS release is not security-qualified because files are “signed”. It is qualified only when independent repository authorization, publisher trust, local privilege boundaries, anti-rollback and negative abuse cases all behave according to the declared trust model.
