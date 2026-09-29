# Batch 5 Report

Batch: 5 — Desktop activation, signed entitlement verification, secure storage

Status: implemented and independently verified.

Created:
- centralized LicenseService implementing ILicenseService
- Ed25519 signed offline-lease verifier using NSec.Cryptography
- Windows DPAPI-protected activation-state store
- persistent installation UUID provider
- trusted server/local timestamp state and clock-rollback detection
- activation window and MVVM activation flow
- startup coordinator that gates the pharmacy shell on a valid cached/online entitlement
- signed-lease integrity/tampering tests

Security behavior:
- plaintext license keys are used only for initial online activation and are not persisted
- the Laravel private signing key never enters the desktop application
- only the Ed25519 public key is required by the desktop release
- cached activation data is protected with Windows DPAPI LocalMachine scope
- signed entitlement device_id and activation_id are matched to the local installation
- local clock rollback beyond the configured tolerance forces online verification
- trial/offline validity is evaluated against signed server timestamps, not DateTime.Now alone
- reinstalling does not create a new server-side trial; the server remains authoritative

UI behavior:
- unactivated installations open the BusinessOS Pharmacy activation window
- Start 7-Day Free Trial opens the existing BusinessOS Pharmacy website registration page
- valid cached entitlements open the native pharmacy shell
- invalid/tampered/expired cached activation returns to verification/activation flow

Configuration:
- BusinessOS:Licensing:SigningPublicKey accepts the Laravel Ed25519 public key in Base64
- release/deployment can inject it with BusinessOS__Licensing__SigningPublicKey
- the repository intentionally does not contain any signing private key

Packages:
- NSec.Cryptography 26.4.0 for production-ready Ed25519 verification
- System.Security.Cryptography.ProtectedData 10.0.0 for Windows DPAPI

Database changes:
- none

Laravel API changes:
- none in this batch; Batch 4 APIs are reused

Verification:
- dotnet restore: passed
- dotnet build Release --no-restore: passed
- build warnings: 0
- build errors: 0
- unit tests: 7 passed, 0 failed
- integration tests: 2 passed, 0 failed
- package vulnerability audit: no vulnerable packages reported
- verification environment: Ubuntu 24.04 / .NET SDK 10.0.401 / net10.0-windows cross-build

Known follow-ups:
- native Windows smoke test is still required to exercise DPAPI and the WPF activation flow on Windows hardware
- production/release packaging must inject the current Laravel Ed25519 public signing key
- Batch 6 will implement tenant pharmacy authentication, roles and permissions

Next batch:
- Batch 6 — authentication, roles and permissions.
