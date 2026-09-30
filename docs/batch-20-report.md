# Batch 20 — Update System

Implemented the native Darmaltoon application update flow for Standalone, Main Pharmacy Server and Client Terminal modes.

## Release trust

- Update metadata is versioned and signed with RSA-PSS/SHA-256.
- The application contains only the trusted public verification key; the release private key stays outside the desktop application.
- Every package is downloaded over HTTPS and must match the signed SHA-256 checksum before staging.
- Unsigned, altered, wrong-channel, incompatible-API or malformed manifests are rejected.
- Downgrades and same-version packages are not installed.

## Version and LAN compatibility

- Darmaltoon now has a centralized application version.
- Main Pharmacy Server reports its application version through the existing pinned-TLS local server identity endpoint.
- Client Terminal update checks verify the paired Main Server version.
- A client update is blocked when the signed manifest requires a newer Main Server.
- LAN API compatibility remains pinned to the existing v1 contract.
- Recommended rolling order remains Main Server first, then Client Terminals.

## Data safety

- Program files and pharmacy data remain physically separated.
- Update staging and rollback files live in the ProgramData updates workspace.
- Update packages are prevented from containing pharmacy.db, Config, licensing, certificates, backups, logs or network secret paths.
- Path traversal and absolute package paths are rejected.
- Standalone and Main Server modes create a verified Batch 19 database backup before installation.
- Client Terminal mode does not replace or back up a local authoritative pharmacy database.
- Main Server updates wait for the desktop process to exit, stop the Local Server Windows Service during file replacement, then restart it.
- Application files are backed up before replacement. Any apply failure restores the previous application files.
- The update agent runs from a copied runner outside the installation directory so it can safely replace updater binaries too.

## Administrator experience

A new Application Updates workspace allows a settings administrator to:

1. check the signed release manifest,
2. see current and available versions,
3. review compatibility/release notes,
4. download and verify the package,
5. install the prepared update.

The application closes only after the package has been fully verified and staged.

## Platform manifest contract

The desktop expects the configured update endpoint to return:

- schema_version
- channel
- version
- api_version
- minimum_supported_version
- minimum_server_version (optional)
- package_url
- package_sha256
- published_at
- release_notes
- signature

The platform must sign the canonical manifest payload with the matching private release key. The desktop app must be deployed with the corresponding public key in BusinessOS:Updater:SigningPublicKeyPem.
