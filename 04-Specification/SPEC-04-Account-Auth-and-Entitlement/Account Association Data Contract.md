# SPEC-04 — Account Association Data Contract

## 1. Purpose

This document defines local non-secret identity/association data required to connect one Windows user profile to one SplitOS Account without confusing Windows identity with SplitOS identity.

---

## 2. Local association object

Per Windows user, local canonical association metadata is:

```text
WindowsUserAccountAssociation
```

Logical fields:

```text
associationId
windowsUserSidReference
accountId
associationState
associatedUtc
lastAuthenticatedUtc
lastEntitlementVersion
lastEntitlementObservedUtc
lastServerUtc
revision
```

`associationId` is a locally generated opaque UUID-like stable identifier for the association instance.

---

## 3. Identity key rules

### Windows side

Windows SID/session authority belongs to Windows.

Local storage MAY persist a normalized/reference form of current Windows SID sufficient to detect accidental profile/store mismatch.

It MUST NOT use user display name as authority.

### SplitOS side

`accountId` is server canonical.

The following are explicitly non-key display metadata:

```text
email
displayName
avatar URL
```

Changing email MUST NOT create a new account association.

---

## 4. Cardinality

v1 local rule:

```text
one Windows user profile
→ at most one ACTIVE SplitOS account association
```

Historical/replaced association records MAY be retained only if necessary for migration/audit and MUST NOT become concurrently active runtime identities.

One SplitOS account may be associated with multiple Windows users/installations according to backend product policy.

---

## 5. Association creation transaction

Association becomes durable only after:

```text
OIDC identity validated
→ backend account fetched
→ entitlement fetch attempted/handled
→ reusable refresh token protected successfully
→ association record persisted successfully
```

If protected token persistence succeeds but association persistence fails, Runtime Host MUST clean up or reconcile the orphaned secret rather than expose ASSOCIATED state.

If association persistence succeeds but secret persistence did not, Runtime MUST mark `REAUTH_REQUIRED`, not ASSOCIATED with a fake reusable session.

---

## 6. Association state values

Recommended durable values:

```text
ACTIVE
REAUTH_REQUIRED
SIGNED_OUT
```

Transient `AUTHENTICATING` / `SIGNING_OUT` remain runtime state and need not be durable unless implementation recovery proves a need.

### ACTIVE

Stable accountId association exists and auth module has usable/recoverable session material or valid product policy.

### REAUTH_REQUIRED

Known association exists but reusable authentication evidence cannot be trusted/used.

Manager may display last known account identity but premium online operations require sign-in.

### SIGNED_OUT

No active account association is authorized for the Windows user.

Implementation may physically delete active row instead of persisting this state; either behavior must preserve one-active-association invariant.

---

## 7. Local entitlement evidence metadata

Normal association/user data MAY retain non-secret evidence metadata:

```text
lastEntitlementVersion
lastPlanDisplayValue
lastCapabilitySummary/hash
lastEntitlementObservedUtc
lastEntitlementValidUntil
```

These fields are UX/startup evidence only.

They MUST NOT independently authorize managed runtime.

Authorization requires:

```text
current online backend entitlement
or
valid protected signed offline assertion
```

---

## 8. Offline assertion association binding

When requesting offline assertion, Runtime sends current:

```text
accountId
installationId
associationId
```

Signed assertion MUST return matching `sub/accountId`, `installationId` and `associationId`.

A new account-switch operation generates a new associationId; old offline assertion therefore cannot authorize the replacement association.

---

## 9. Account switch

Account A → Account B procedure:

```text
block new premium operation creation
→ safe runtime convergence as needed
→ revoke/clear Account A tokens/offline proof
→ mark/remove Account A association
→ authenticate Account B
→ create new associationId
→ persist Account B secret + association
→ fetch entitlement
→ recompute runtime access
```

Do not reuse associationId across different account IDs.

---

## 10. Sign-out and user data

Sign-out clears:

```text
active account association
protected auth credentials
offline entitlement assertion
current entitlement authorization state
```

Sign-out does **not** by default delete:

```text
Game Profiles
user preferences
Shared App preferences
```

These are separate user-owned product data.

Manager may offer a distinct destructive action:

```text
Remove local SplitOS user data
```

which belongs to UX/persistence policy, not normal sign-out.

---

## 11. Windows profile copy/restore

If `user.db`/association metadata is copied/restored but DPAPI credential blob cannot decrypt:

```text
association known
→ REAUTH_REQUIRED
```

Do not generate a new refresh token locally.

If Windows SID reference does not match current profile/context:

```text
LOCAL_ASSOCIATION_CONTEXT_MISMATCH
→ no automatic premium authorization
→ require explicit re-association/sign-in
```

---

## 12. Concurrency / revision

Association writes SHOULD use optimistic revision semantics compatible with SPEC-03:

```text
expectedRevision
→ update
→ newRevision
```

A stale Manager/UI operation cannot overwrite a newer account switch/sign-out state.

Only Runtime Host Product Identity module is semantic writer.

---

## 13. UI projection

Manager may receive safe projection:

```text
associationState
accountId opaque reference
displayName
masked/normal email for display
plan display label
runtime access status/reason
```

Manager MUST NOT receive refresh token/offline assertion.

---

## 14. Acceptance criteria

1. Windows and SplitOS account IDs remain separate;
2. email is not canonical identity key;
3. at most one active SplitOS account association exists per Windows user;
4. associationId changes when switching accounts;
5. offline assertion is bound to associationId;
6. local cached plan metadata cannot grant PRO;
7. account switch clears old auth/offline evidence first;
8. sign-out does not destroy Game Profiles by default;
9. DPAPI restore mismatch results in REAUTH_REQUIRED;
10. Manager never receives reusable credentials.
