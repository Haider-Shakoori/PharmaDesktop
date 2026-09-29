# Batch 1 Report

Batch: 1 — Repository setup, Laravel audit, parity matrix, architecture

Status: implementation committed; CI verification required before merge.

Created:
- .NET 10 WPF clean-architecture solution
- Domain/Application/Infrastructure/Persistence/Licensing/Sync/Printing/Updater projects
- Unit and integration test projects
- DI/configuration/Serilog application host
- SQLite persistence foundation
- Laravel audit, parity matrix, desktop architecture
- Windows GitHub Actions build/test workflow

Changed:
- README from repository placeholder to BusinessOS Pharmacy Desktop overview

Removed:
- none

Database changes:
- none to Laravel
- no production data touched
- desktop SQLite schema is not yet implementing pharmacy entities

API changes:
- none
- current Laravel /api/v1/license/activate contract documented for future desktop use

Tests defined:
- signed entitlement validity-window behavior
- centralized feature lookup
- SQLite connectivity smoke test

Build/tests:
- must be taken from the GitHub Actions run for this commit; do not mark green until that run completes.

Known issues:
- signed Laravel lease does not yet cryptographically include the returned plan feature set
- purchase-return parity is not present in the audited Laravel tenant schema/routes
- prior Flutter Windows documentation exists in Laravel and must not be mistaken for the new C# client direction

Next batch:
- Batch 2 — deepen application host/configuration/logging, options validation, exception handling, environment paths, and foundational services after CI is green.
