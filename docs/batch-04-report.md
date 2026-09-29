# Batch 4 Report

Batch: 4 — Licensing backend integration

Status: completed and verified.

Created:
- ILicenseActivationClient transport abstraction
- LicenseActivationClient for initial activation and signed-lease refresh
- structured LicenseApiException with retryability and validation errors
- desktop refresh request contract
- unit coverage for activation metadata, Bearer lease refresh, validation failures, and retryable server failures

Changed:
- activation request now carries device model, OS version, build number, and Windows platform metadata
- licensing options now include /api/v1/desktop/license/refresh
- licensing DI registers the typed transport
- desktop appsettings includes the refresh endpoint
- options validation covers both activation and refresh API paths

Laravel companion work:
- accepts desktop OS/model/build metadata
- signs entitlements and lifecycle timestamps inside the Ed25519 offline lease
- adds purpose=offline_lease
- adds POST /api/v1/desktop/license/refresh
- refresh revalidates device, activation, tenant, license/version and subscription state
- plaintext license key is only needed for initial activation
- companion PR Haider-Shakoori/Pharmacy#50 merged as 146b6290dc628ce4dfef76d82096a738efe20688

Database changes:
- none

Desktop API changes:
- POST /api/v1/license/activate remains the initial activation endpoint
- POST /api/v1/desktop/license/refresh is the post-activation renewal endpoint

Desktop verification:
- dotnet build Release: passed
- build warnings: 0
- build errors: 0
- unit tests: 10 passed, 0 failed
- integration tests: 2 passed, 0 failed
- vulnerable package audit: no vulnerable packages reported
- verification environment: Ubuntu 24.04, .NET SDK 10.0.401, net10.0-windows cross-build

Laravel verification:
- push CI: passed
- pull-request CI: passed
- Laravel regression: 106 tests / 570 assertions passed
- Pint: 326 files passed
- Composer security audit: passed
- production frontend build: passed
- Release Candidate web UAT: passed
- Android Drift generation, formatting, static analysis and complete Flutter regression suite: passed

Known follow-ups:
- Batch 5 will verify Ed25519 signatures in C#, convert signed claims into EntitlementSnapshot, persist activation material with Windows protection, create persistent installation identity, enforce clock rollback protection, and implement the activation UI
- GitHub Actions on the private desktop repository still fails before executing steps; independent VPS verification remains the current desktop build evidence

Next batch:
- Batch 5 — desktop activation, signed entitlement verification, secure storage.
