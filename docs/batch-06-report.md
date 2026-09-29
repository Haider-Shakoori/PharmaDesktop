# Batch 6 Report

Batch: 6 — Authentication, roles and permissions

Status: implemented and independently verified.

Architecture:
- license activation and pharmacy employee login remain separate concerns
- the activated Windows lease identifies the tenant/device
- pharmacy staff authenticate with the existing tenant-scoped Laravel users
- BusinessOS platform administrators/operators remain web-only
- the desktop consumes the existing Laravel role and permission codes; it does not invent a parallel RBAC model

Laravel companion:
- companion PR: Haider-Shakoori/Pharmacy#51
- merged commit: 2b43da076c234fe8a42e4bf84c26fc4e9827d666
- POST /api/v1/desktop/session/login
- POST /api/v1/desktop/session/refresh
- desktop_access token is Ed25519 signed and bound to tenant, activation, device and user
- signed claims include user name, email, roles and permissions
- login verifies active Windows activation, license, subscription health and tenant user credentials
- refresh re-reads current user/role/permission state so online RBAC changes take effect
- expired desktop access tokens cannot silently refresh; re-authentication is required

Desktop authentication:
- native pharmacy staff login window
- signed desktop_access verifier using the same Laravel Ed25519 public key
- IUserSessionService manages online login, online refresh, offline login and logout
- user session identity, roles and permissions are validated against the signed token
- main navigation is derived from the signed permission snapshot
- IPermissionAuthorizer provides fail-closed application-service authorization through Demand(permission)

Offline authentication:
- plaintext passwords are never persisted
- optional offline sign-in derives a verifier using PBKDF2-HMAC-SHA256
- 600,000 PBKDF2 iterations
- random 16-byte salt and 32-byte derived hash
- password comparisons use fixed-time comparison
- signed staff session and PBKDF2 verifier are protected with Windows DPAPI CurrentUser scope
- offline sign-in requires both an unexpired signed employee session and a valid signed pharmacy license lease
- tenant, activation and device IDs must match the local activated installation
- clock rollback protection continues to apply
- wrong online credentials never fall back to the cached offline credential

Role/permission behavior:
- exact existing Laravel permission codes are used
- examples wired into the shell include dashboard.view, pos.sell, medicines.manage, inventory.manage, purchases.manage, customers.manage, reports.view, daily_closing.perform, users.manage and roles.manage
- hiding unauthorized navigation is only a UX layer; IPermissionAuthorizer is the enforcement layer for operational services

Database changes:
- none

Desktop configuration changes:
- SessionLoginPath defaults to /api/v1/desktop/session/login
- SessionRefreshPath defaults to /api/v1/desktop/session/refresh
- no secret or private signing key is committed

Desktop verification:
- dotnet build Release --no-restore: passed
- build warnings: 0
- build errors: 0
- unit tests: 12 passed, 0 failed
- integration tests: 2 passed, 0 failed
- package vulnerability audit: no vulnerable packages reported
- verification environment: Ubuntu 24.04 / .NET SDK 10.0.401 / net10.0-windows cross-build

Laravel verification:
- GitHub Actions CI: passed
- Laravel tests: 110 passed / 595 assertions
- Pint formatting: passed
- Composer security audit: passed
- frontend dependency install/build: passed

Known follow-ups:
- native Windows smoke testing is still required for the WPF login flow and DPAPI CurrentUser behavior before release packaging
- future operational application services must call IPermissionAuthorizer rather than relying only on permission-filtered navigation
- Batch 7 will implement the SQLite/EF Core local persistence model

Next batch:
- Batch 7 — SQLite/EF Core local persistence.
