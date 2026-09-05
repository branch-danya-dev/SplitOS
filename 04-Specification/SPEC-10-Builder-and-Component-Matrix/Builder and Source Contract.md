# SplitOS — Builder and Windows Source Contract

## 1. Scope

This document defines the v1 boundary between:

```text
external Windows installation source
and
SplitOS Media Builder
```

The Builder owns validation and transformation. It does not own Microsoft Windows binaries or Windows licensing.

## 2. v1 source-acquisition policy

Production v1 MUST support:

```text
USER_PROVIDED_SOURCE
```

where the user supplies a Windows installation source obtained through a Microsoft-authorized channel.

Automatic source download is `OPEN` until legal/licensing and technical source-acquisition validation is complete.

The Builder MUST NOT rely on an unofficial Microsoft download endpoint or third-party repackaged ISO as a product dependency.

## 3. Accepted container forms

The first implementation SHOULD accept:

```text
ISO file
or
mounted/extracted official Windows setup tree
```

A source is not accepted merely because it contains `setup.exe` or `install.wim`.

## 4. SourceIdentity

The Builder creates an immutable `SourceIdentity` before transformation.

Minimum fields:

```text
sourceIdentityId
sourceType
sourceFileDigest        // when a single ISO/file is supplied
bootImageDigest(s)
installImageDigest
installImageFormat     // WIM / ESD where supported
selectedImageIndex
edition
architecture
language
Windows version/build
image name/description
sourceCatalogId        // release-owned known-source catalog entry or null
validationLevel
observedAt
```

`selectedImageIndex` is mandatory because one installation image may contain multiple editions/images.

## 5. Source validation levels

```text
KNOWN_RELEASE_SOURCE
VALIDATED_UNKNOWN_HASH
UNSUPPORTED
INVALID_OR_CORRUPT
```

### KNOWN_RELEASE_SOURCE

Requires an exact release-owned source-catalog match for the selected source identity/fingerprint.

This is the preferred supported path.

### VALIDATED_UNKNOWN_HASH

Metadata/image structure may be parseable and internally consistent, but the source artifact does not match an approved release source catalog entry.

This state MUST NOT silently proceed as a normal supported SplitOS build.

A future developer/diagnostic mode MAY inspect it, but production build output is unsupported unless a release policy explicitly authorizes that source.

## 6. Validation sequence

```text
read-only input
↓
calculate source fingerprints
↓
inspect setup tree
↓
inspect install image metadata/indexes
↓
select requested edition/index
↓
compare release source constraints
↓
verify required servicing prerequisites
↓
SUPPORTED or reject
```

Source files are never modified in place.

## 7. Required release source constraints

A Build Manifest source constraint MUST include at least:

```text
architecture
supported edition(s)
minimum/exact Windows build policy
language policy
accepted image format(s)
known source catalog references where required
```

Release policy SHOULD pin an exact known Windows base rather than using an unbounded rule such as:

```text
Windows 11 >= X
```

unless compatibility verification genuinely covers the range.

## 8. Working copy

The Builder MUST transform a working copy/staging area.

```text
original source
→ immutable/read-only reference

working source
→ transformed
→ may be discarded safely
```

A failed build MUST NOT leave the user's original ISO/source modified.

## 9. Windows image inventory

Before mutation, the Builder records at least:

```text
OS packages
optional features
provisioned AppX packages
capabilities relevant to the release
boot/setup images
available image indexes
release-required directories/files
```

Additional inventories MAY include drivers and release-specific component identifiers.

The inventory is evidence used by manifest preconditions; it is not the Component Matrix itself.

## 10. Toolchain compatibility

The Builder MUST use a servicing toolchain version supported for the target Windows image.

The Windows ADK/DISM toolchain identity used for a build is included in the BuildReceipt.

The Builder MUST NOT silently fall back from a required supported servicing operation to an undocumented registry/file hack when DISM reports that the operation is unsupported.

## 11. Source rejection conditions

Production build MUST stop before destructive transformation when any mandatory condition fails, including:

```text
source cannot be parsed
selected index does not exist
architecture mismatch
edition unsupported
Windows build outside release policy
known-source fingerprint mismatch when exact match is required
required image/boot structure missing
image corruption/integrity failure
servicing toolchain incompatible
manifest/source schema incompatibility
```

## 12. Windows licensing boundary

SplitOS does not grant a Windows license.

The Builder/installer MUST preserve the conceptual distinction:

```text
Windows license / activation
!=
SplitOS Account
!=
SplitOS entitlement
```

## 13. Reproducibility identity

Byte-identical WIM/ISO output is not required as the primary product identity because packaging/compression metadata can vary.

The supported baseline is instead bound semantically to:

```text
SourceIdentity
+
BuildManifest digest/version
+
Component Matrix version
+
SplitOS package digests
+
Builder/toolchain version
+
verified postconditions
```

These values form the basis of `BuildReceipt` and `InstalledBaselineIdentity`.

## 14. OPEN

- automatic Microsoft-authorized source acquisition;
- exact initial Windows edition/language support set;
- whether ESD input is accepted directly or normalized to WIM working form;
- source catalog publication/update mechanism;
- whether Builder runs only on Windows or later gains another host environment.
