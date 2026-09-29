# Batch 1 Report

Batch: 1 — Repository setup, Laravel audit, parity matrix, architecture

Status: foundation implemented and independently verified; Windows CI is configured on the pull request.

Created:
- .NET 10 WPF clean-architecture solution
- Domain/Application/Infrastructure/Persistence/Licensing/Sync/Printing/Updater projects
- Unit and integration test projects
- DI/configuration/Serilog application host
- SQLite persistence foundation
- Laravel audit, parity matrix, desktop architecture
- Windows GitHub Actions restore/build/test/vulnerability workflow

Changed:
- README from repository placeholder to BusinessOS Pharmacy Desktop overview
- EF Core SQLite updated to 10.0.12 after restore exposed a vulnerable older transitive SQLite package
- CI tests run serially to avoid unnecessary parallel test-host memory pressure

Removed:
- none

Database changes:
- none to Laravel
- no production data touched
- desktop SQLite schema is not yet implementing pharmacy entities

API changes:
- none
- current Laravel /api/v1/license/activate contract documented for future desktop use

Verification:
- dotnet restore: passed
- dotnet build Release --no-restore: passed, 0 warnings, 0 errors
- unit tests: 2 passed, 0 failed
- integration tests: 1 passed, 0 failed
- dotnet list package --vulnerable --include-transitive: no vulnerable packages reported

Verification environment:
- .NET SDK 10.0.401 on the connected Linux verification host
- Windows-targeted WPF projects cross-compiled successfully
- final Windows CI workflow performs restore, build, unit tests, integration tests, and vulnerability audit

Known issues / parity gaps:
- signed Laravel lease does not yet cryptographically include the returned plan feature set
- purchase-return parity is not present in the audited Laravel tenant schema/routes
- prior Flutter Windows documentation exists in Laravel and must not be mistaken for the new C# client direction

Next batch:
- Batch 2 — deepen application host/configuration/logging, options validation, exception handling, environment paths, and foundational services.
