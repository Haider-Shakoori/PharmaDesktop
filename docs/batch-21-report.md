# Batch 21 — Windows Installer / EXE Packaging

Batch 21 adds the Windows release and deployment system for Darmaltoon.

## Release artifacts

The Windows release workflow produces three self-contained x64 Setup.exe packages plus matching portable ZIPs:

- Standalone Pharmacy
- Main Pharmacy Server
- Client Terminal

Each release also includes SHA-256 checksums and a machine-readable release manifest.

The desktop, updater agent and Main Server are published self-contained for `win-x64`; customer PCs do not need a separate .NET runtime installation.

## Installation ownership

Application binaries are installed under `Program Files\BusinessOS\Darmaltoon`.

Authoritative and persistent pharmacy state remains under `ProgramData\BusinessOS\Pharmacy`, including:

- `pharmacy.db`
- backups
- activation/licensing state
- network configuration and terminal secrets
- local server certificates
- logs
- update staging/rollback data

The installer and uninstaller intentionally do not own or delete this ProgramData tree. Upgrades replace application binaries only.

## Deployment modes

Each package writes only a non-authoritative first-run deployment hint into the application installation directory. If an existing network configuration is present, it wins and is never overwritten by an installer upgrade.

On a fresh PC the existing Darmaltoon deployment wizard opens with the packaged mode preselected:

- Standalone initializes a one-PC pharmacy.
- Main Server initializes the authoritative local database and server configuration.
- Client Terminal opens the existing secure discovery/pairing workflow and does not create an authoritative local pharmacy database.

## Main Server deployment

The Main Server Setup.exe additionally installs `BusinessOS Pharmacy Local Server` as an automatic Windows Service, but does not start it before Darmaltoon has completed first-run server configuration. After successful server setup/activation the desktop ensures Private-profile TCP/UDP firewall rules and starts the service.

Upgrades stop the service before replacing binaries and restart it afterward when it was already installed. Uninstall removes the service and its firewall rules while preserving pharmacy data/configuration.

## Upgrade and update compatibility

- The same installer application ID is retained between versions for in-place upgrades.
- Existing ProgramData is not replaced by a new deployment package or mode hint.
- Batch 20's update agent is shipped with its own self-contained runtime under `Updater`, so application updates do not depend on a machine-wide .NET installation.
- The updater copies itself out of the installation directory before replacing application binaries.

## Windows release validation

The Windows workflow performs:

1. Release build and full unit/integration tests.
2. Self-contained `win-x64` publish for Desktop, Updater and Local Server.
3. Compilation of all three Setup.exe packages.
4. Clean Standalone installation and executable launch verification.
5. Synthetic older-version install followed by in-place upgrade to the current version.
6. ProgramData sentinel verification across install, upgrade and uninstall.
7. Main Server service/startup-type and server payload verification.
8. Client Terminal verification that no Main Server service is installed.
9. Start Menu shortcut and Windows version-resource verification.
10. Release SHA-256 manifest verification.

The Linux VPS cross-validates the Windows self-contained publish and all managed tests; actual installer execution is performed by the Windows CI runner.

## Production signing

The release script supports optional Authenticode signing through CI secrets and can inject the licensing/update public verification keys at packaging time. Production releases should use those secrets; private signing keys are never committed to the repository.

The packaging workflow uses Inno Setup for Setup.exe generation. Its publisher requests commercial users to obtain a commercial license; CI supports supplying that compiler license through the `INNO_SETUP_LICENSE_KEY` secret.
