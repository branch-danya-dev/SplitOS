# SPEC-14 — Release Readiness Evidence & Sign-off

## 1. Purpose

Defines the final evidence package and decision process that converts a tested SplitOS release candidate into an approved production release.

This layer does not re-run every test. It proves that the candidate, support profile, gate results and known exclusions form one coherent release decision.

---

# 2. ReleaseReadinessRecord

A final release decision is represented conceptually by one immutable `ReleaseReadinessRecord`.

Minimum content:

```text
readinessRecordId
createdAtUtc
releaseCandidateId
releaseId
releaseSequence
securityEpoch
acceptanceProfileId/version
BuildReceipt identity
Release Envelope / trusted metadata identity
candidate artifact digest set
Component Matrix version/digest
Windows Base support set
Game Client capability matrix
Game/config adapter support set
required update source edges
required recovery edges
gate results
mandatory case summary
performance threshold/result summary
compatibility matrix summary
known unsupported/experimental scope
known issues within supported scope
open-risk disposition
sign-offs
final verdict
```

---

# 3. Final verdict states

```text
INCOMPLETE
BLOCKED
READY_FOR_LIMITED_VALIDATION
RELEASE_CANDIDATE
PRODUCTION_READY
REJECTED
```

Only `PRODUCTION_READY` authorizes normal production publication/activation for the declared profile.

---

# 4. Preconditions for RELEASE_CANDIDATE

A candidate may be called `RELEASE_CANDIDATE` only when:

- candidate artifacts are frozen;
- production-equivalent signing/repository metadata path is available;
- ReleaseAcceptanceProfile is frozen;
- no known code/config changes are pending for the candidate;
- required L6 test suites can operate on exact candidate artifacts;
- support claims are explicit.

A mutable nightly build is not a release candidate.

---

# 5. Preconditions for PRODUCTION_READY

All must hold:

```text
GATE-00 PASS
GATE-01 PASS
GATE-02 PASS
GATE-03 PASS
GATE-04 PASS
GATE-05 PASS for declared gaming scope
GATE-06 PASS
GATE-07 PASS
GATE-08 PASS
GATE-09 PASS
GATE-10 PASS
GATE-11 PASS
GATE-12 PASS
```

plus:

```text
no mandatory case FAIL
no mandatory case BLOCKED
no unresolved release-scoped OPEN
no hidden unsupported capability advertised as supported
no stale acceptance evidence after artifact/profile change
```

---

# 6. Known issues

Known issues are allowed only when they do not invalidate a mandatory support claim or release-blocking invariant.

Each known issue records:

```text
issueId
affected capability/matrix cells
severity
user-visible effect
workaround if any
why release gate still passes
owner
planned disposition
release notes disclosure requirement
```

Examples potentially acceptable:

```text
experimental Battle.net adapter fails on current client
→ not in supported production scope

in-game optional panel unavailable for one exclusive-fullscreen title
→ panel capability excluded for that cell; game launch/mode remain supported
```

Examples not acceptable as “known issue” while shipping affected support claim:

```text
rollback can lose user Game Profiles
Broker accepts cross-session machine mutation
FREE sometimes remains PRO after expired assertion
update can commit unverified target
signed old release can bypass anti-rollback
```

---

# 7. No critical waivers

There is no release-manager override equivalent to:

```text
ignore GATE-08 FAIL
ignore data-loss case
ignore recovery failure
ship anyway
```

If product leadership wishes to ship despite a failing `SCOPE_BLOCKING` optional capability, the correct operation is to revise/remove the support claim and produce a new acceptance profile version.

Global safety/security invariants cannot be removed from scope without changing the product requirements/architecture baseline through SSAD change discipline.

---

# 8. Sign-off roles

Minimum conceptual approvals:

```text
Product/System Analysis
→ semantic support scope and requirement conformance

Engineering
→ implementation candidate identity / known technical risks

QA / Release Verification
→ gate execution/evidence completeness

Compatibility
→ Windows/hardware/client support matrix

Security
→ GATE-08 / release trust / privileged-boundary evidence

Release Owner
→ publication decision after all required approvals
```

Exact organization/job titles may differ, but separation of evidence ownership and final publication authorization should remain explicit.

---

# 9. Sign-off statements

Each sign-off should be an explicit assertion, not a generic “approved”.

Examples:

### Analysis/System sign-off

```text
The declared ReleaseAcceptanceProfile matches the product/system scope represented by the canonical requirements and specifications.
```

### QA/Verification sign-off

```text
All mandatory release gates were executed against the stated immutable candidate and required matrix cells; recorded results are complete.
```

### Compatibility sign-off

```text
The published support matrix does not exceed the matrix cells/capabilities for which required compatibility evidence passed.
```

### Security sign-off

```text
Required release-security and privileged-boundary acceptance suites passed using production-equivalent trust configuration; no known mandatory security gate remains failed/blocked.
```

### Release-owner sign-off

```text
The exact approved candidate and metadata set may be promoted to production for the declared acceptance profile.
```

---

# 10. Evidence bundle structure

Conceptual release evidence package:

```text
ReleaseEvidence/
├── readiness-record.json
├── acceptance-profile.json
├── candidate-identity.json
├── build/
│   ├── BuildReceipt
│   └── build-verification-summary
├── gates/
│   ├── GATE-00.json
│   ├── ...
│   └── GATE-12.json
├── cases/
│   └── result index / machine-readable results
├── compatibility/
│   ├── matrix.json
│   └── coverage-summary
├── performance/
│   ├── thresholds.json
│   └── results
├── security/
│   └── acceptance-summary / artifact refs
├── update-recovery/
│   └── edge-results
├── diagnostics/
│   └── privacy/supportability summary
├── known-issues.json
└── signoffs/
```

Raw large traces/dumps may be stored by reference in controlled artifact storage rather than embedded in one package.

---

# 11. Evidence integrity

Release evidence should be content-addressed or otherwise integrity-protected enough to prevent accidental substitution.

At minimum:

- candidate identity hashes are immutable;
- acceptance profile version immutable after sign-off;
- result records reference exact candidate/profile;
- sign-off references exact readiness record;
- promotion pipeline verifies candidate being promoted matches approved identity.

The production release must not be rebuilt from source after approval and assumed equivalent unless reproducibility/identity policy explicitly re-verifies exact output.

---

# 12. Promotion guard

Production promotion tooling MUST verify:

```text
requested ReleaseCandidateId
== approved ReleaseReadinessRecord candidate
```

and verify required signed metadata/artifact identities.

If a new artifact hash appears after sign-off:

```text
promotion blocked
→ new candidate / affected re-verification
```

---

# 13. Support matrix publication

Release notes/product support information must be generated/reviewed against the exact accepted matrix.

Do not advertise:

```text
Battle.net supported
all Windows 11 supported
all Xbox/Game Pass titles supported
all monitors/controllers supported
```

unless the accepted profile actually says so.

The public claim should preserve distinctions such as:

```text
Steam — supported capabilities X/Y/Z
Epic — supported capabilities X/Y/Z
Microsoft Gaming — partial supported local registered-title capabilities
Battle.net — experimental / not production-supported
```

where that remains the accepted release posture.

---

# 14. Post-release invalidation

After production publication, new evidence may invalidate compatibility/security assumptions.

Examples:

```text
new Steam client breaks library parser
Windows patch changes component dependency
signing key compromise
GPU driver causes display regression
```

Response:

```text
new evidence
→ Compatibility/Security decision
→ restrict support / revoke knowledge / publish fix
→ update requirements/spec if semantics change
→ new release qualification where needed
```

The old readiness record remains historical evidence; it is not rewritten to pretend the original decision never occurred.

---

# 15. Emergency release qualification

Security hotfixes may use an accelerated profile, but cannot skip the invariants affected by the change.

A security emergency release should at minimum run:

```text
artifact/trust validation
install/update from supported sources
Broker/runtime smoke where affected
critical mode/game smoke where affected
update/reboot/rollback safety
security regression suite for changed boundary
user-data preservation
release evidence/sign-off
```

Risk-based reduction of unrelated matrix depth may be allowed by release process, but non-waivable gates remain non-waivable.

---

# 16. Rollback of production release

If production release is withdrawn:

- repository metadata/revocation/support status updated according to SPEC-12;
- ordinary downgrade still cannot bypass anti-rollback;
- authorized recovery paths remain explicit;
- support matrix/release notes updated;
- affected readiness record retained for incident analysis.

---

# 17. Final Detailed Specification completion marker

After SPEC-14 acceptance by review, the repository can mark Detailed Specification baseline complete:

```text
SPEC-01 .. SPEC-14 = READY FOR REVIEW / BASELINED
```

This does **not** mean the product is production-ready today.

It means:

```text
architecture/specification is sufficiently explicit
→ implementation can be planned
→ each future implemented release has an executable verification contract
```

---

# 18. Next lifecycle phase

The SSAD handoff becomes:

```text
Detailed Specification complete
↓
Grooming / implementation planning
↓
Delivery slices
↓
Verification execution
↓
Release readiness
```

Implementation discoveries that contradict the spec return through the normal change chain rather than being hidden in exceptions.

---

# 19. Result

`PRODUCTION_READY` is an evidence-backed release decision over one immutable candidate and one explicit support profile — not a subjective milestone label.
