# Key Rotation, Revocation and Compromise Response

## 1. Purpose

This document defines planned rotation, emergency revocation and incident handling for SplitOS release-security keys.

A rotation is normal lifecycle maintenance.

A compromise is an incident.

They MUST NOT be handled as the same workflow.

---

## 2. Planned rotation

Every active production signing key has a planned replacement path before expiration or operational end-of-life.

Rotation must preserve uninterrupted validation:

```text
old trusted key
→ signed authorization of new trust state
→ overlap/transition
→ new key active
→ old key retired
```

There is never a supported gap where signature validation is disabled.

---

## 3. Root rotation

Root metadata rotation follows the adopted TUF root update procedure.

For root `N → N+1`:

```text
root N threshold signs root N+1
+
root N+1 threshold signs root N+1
```

The client walks roots sequentially.

It MUST NOT jump directly from an old trusted root to an arbitrary far-future root without verifying each required root transition according to the adopted TUF client workflow.

---

## 4. Delegated role rotation

For a delegated role such as `splitos-release`:

```text
trusted Targets metadata
→ replaces/extends delegated key set
→ new delegated metadata signed by newly authorized key
→ old key retired
```

The old role key cannot authorize itself after removal from trusted delegation metadata.

---

## 5. Online key rotation

Online keys SHOULD rotate more frequently than offline root keys.

Candidates:

```text
RELEASE_METADATA
KNOWLEDGE_METADATA
SNAPSHOT
TIMESTAMP
```

Rotation cadence is operational policy and will be acceptance-tested in SPEC-14.

The design must support rotation without shipping a new executable solely to recognize a new key.

---

## 6. Authenticode certificate renewal

Code-signing certificate renewal is handled through authenticated publisher policy metadata.

During planned transition, metadata MAY authorize both old and new publisher identities for a bounded release window.

After transition:

```text
new signer = active
old signer = retired for new releases
```

Historically timestamped artifacts are evaluated under signed historical/incident policy rather than blanket rejection.

---

## 7. Revocation object

SplitOS security metadata needs explicit revocation semantics.

Conceptual `SecurityRevocationSet`:

```text
revocationSetId
version
issuedAt
securityEpoch
revokedKeyIds[]
revokedPublisherIds[]
revokedReleaseIds[]
minimumAllowedReleaseSequence
minimumAllowedSecurityEpoch
hardRecoveryBlocks[]
reasonCodes[]
```

It is authenticated through trusted release-security metadata.

---

## 8. Soft vs hard consequence

Revocation may affect different actions differently.

Examples:

### Block new use

```text
key compromised
→ do not accept new metadata/signatures from key
```

### Block activation

```text
release vulnerable
→ staged/recovery activation denied
```

### Force recovery target invalidation

```text
previous release has critical vulnerability
→ persisted hard recovery block
→ capsule remains physically present but is no longer an automatic recovery target
```

### Installed release response

May range from:

```text
warning + update required
```

to:

```text
managed runtime disabled until safe release available
```

Windows base desktop remains preferred safe fallback.

---

## 9. Timestamp key compromise

Impact:

- attacker may interfere with freshness signaling if repository access is also controlled;
- attacker cannot sign Targets/delegated release metadata;
- attacker cannot sign artifacts.

Response:

```text
remove old timestamp key from Root
→ authorize new timestamp key
→ publish new root chain
→ publish fresh timestamp
```

Client version/expiry checks remain mandatory.

---

## 10. Snapshot key compromise

Impact is limited to repository consistency metadata.

Response:

- rotate snapshot key through trusted Root;
- republish consistent snapshot;
- preserve client rollback/version floors;
- investigate whether malicious mix-and-match metadata was served.

A compromised snapshot key cannot legitimately create a new delegated target signature.

---

## 11. Release metadata key compromise

This is a high-severity event.

Immediate response:

```text
stop release signer
revoke delegated key through Targets metadata
rotate release role key
raise security epoch if needed
publish revocation set
invalidate malicious/untrusted release IDs
```

Defense in depth:

Executable artifacts still require independent allowed Authenticode publisher signatures.

However non-executable release/knowledge semantics may still be dangerous, so compromise is treated as critical even without code-signing compromise.

---

## 12. Knowledge key compromise

Response:

- revoke knowledge role key;
- invalidate affected knowledge target IDs;
- fall back to last known-good knowledge where safe;
- disable capabilities whose correctness depends on compromised knowledge if no safe version is known.

Typed policy catalogs MUST NOT silently become arbitrary privileged commands.

---

## 13. Recovery key compromise

Recovery key compromise may authorize unsafe downgrade edges.

Response:

```text
revoke recovery delegated key
→ publish new recovery role key
→ publish hard recovery blocks / security floor
→ mark affected capsules non-automatic
```

A capsule whose recovery authorization key is revoked may still contain valid previous release bytes, but those bytes are no longer automatically trusted as an allowed rollback path.

If Windows usability is otherwise at risk, user-facing recovery may offer Windows-native alternatives rather than bypass the recovery signature policy.

---

## 14. Authenticode key compromise

Immediate response:

```text
stop code signing
request certificate revocation
revoke publisher identity in SplitOS security metadata
rotate certificate/key
rebuild/resign affected future release artifacts
```

Repository authorization still prevents a stolen code-signing key from publishing arbitrary binaries into trusted releases without a valid release metadata path.

---

## 15. Root threshold compromise

If enough root keys are compromised to satisfy the root threshold, the fundamental repository trust anchor is compromised.

This is a catastrophic incident.

The product MUST NOT claim an ordinary online self-heal is trustworthy under the same compromised root.

Response requires an out-of-band trust recovery procedure, potentially including:

- new independently distributed product root;
- signed/notarized customer security notice;
- recovery/install media refresh;
- explicit user/admin trust rebootstrap.

The exact business/out-of-band distribution process is outside v1 runtime automation and must be documented operationally before production launch.

---

## 16. Compromise time ambiguity

Incident responders may not know the exact first malicious signature time.

Security policy therefore MUST support revoking by:

```text
keyId
releaseId
releaseSequence range
securityEpoch floor
publisher identity
```

rather than requiring perfect timestamp attribution.

---

## 17. Offline clients

Offline clients only know the last trusted revocation state.

When reconnecting, security metadata refresh runs before optional update activation.

If new revocation metadata makes the current release unsafe:

```text
refresh security state
→ classify installed release
→ require safe update/recovery
```

The updater MUST NOT apply cached staged content first and refresh revocations later.

---

## 18. Emergency publication

Emergency releases use the same trust chain as normal production releases.

Forbidden:

```text
incident
→ disable signature checks
→ upload emergency ZIP
```

Emergency speed is achieved by pre-designed signing/approval paths, not by removing controls.

---

## 19. Rotation audit

Every rotation/revocation records:

```text
old keyId
new keyId if applicable
role
reason
root/targets metadata versions
effective time
approvals
signing events
revocation set version
```

These records feed SPEC-13 security audit events.

---

## 20. Recovery after false positive

A mistaken revocation is itself a security-sensitive event.

Undoing it requires a newer trusted signed security policy/version.

Local UI/debug flags cannot un-revoke a key.

---

## 21. Result

Rotation and incident handling preserve one invariant:

```text
old trust state
→ cryptographically authorized new trust state
```

There is no supported `temporarily trust everything` mode.
