# Batch 7 Report

Batch: 7 — SQLite / EF Core local persistence

Status: implemented and independently verified.

Scope decision:
- Batch 7 establishes the local persistence foundation only.
- Medicine, inventory, purchasing, customer, POS, expense and daily-closing schemas remain in their scheduled feature batches.
- Backup/restore remains Batch 19.
- Offline synchronization remains Batch 18.

Local database:
- SQLite + Entity Framework Core 10.0.12
- persistent database path remains under the configured BusinessOS Pharmacy application-data root
- production default remains C:\ProgramData\BusinessOS\Pharmacy\pharmacy.db
- EF Core migrations are the only supported schema-creation/update path
- initial migration: 20260929202545_InitialLocalPersistence
- design-time DbContext factory added for repeatable migration generation

Initial technical schema:
- local_database_identity
  - binds the database to exactly one BusinessOS pharmacy tenant
  - records a stable database instance UUID
  - records created/last-opened timestamps
- local_settings
  - stores non-secret local application preferences/state only
  - rejects authentication/licensing secret key names
- local_sequences
  - atomic SQLite UPSERT + RETURNING sequence allocator
  - intended for later offline document numbering

Tenant isolation:
- database migration and tenant binding occur after a valid signed license entitlement and before pharmacy staff login
- first valid tenant binds the database
- a later activation for a different tenant cannot silently rebind or overwrite the database
- mismatch is fail-closed and the desktop shuts down after showing a protective warning
- data reset/restore is intentionally deferred to the supported maintenance/backup workflow

SQLite reliability configuration:
- ReadWriteCreate mode
- connection pooling enabled
- shared cache enabled
- foreign keys enabled on connections
- 5-second default timeout
- WAL journal mode established during initialization
- synchronous=NORMAL and busy_timeout=5000 applied whenever EF opens a connection

Application boundaries:
- ILocalDatabaseInitializer
- ILocalSettingsStore
- ILocalSequenceService
- EF Core and raw SQLite details remain inside the Persistence project
- later business modules should add their repository/application interfaces rather than exposing DbContext to UI code

Security:
- protected license/user-session data remains outside SQLite in DPAPI-backed stores
- local_settings rejects keys containing password, access_token, refresh_token, license_key, private_key, secret or credential
- no secret or database credentials are committed
- SQLite contains business/application data, not plaintext authentication material

Startup behavior:
1. validate signed license entitlement
2. activate online if needed
3. run EF Core migrations
4. bind/validate local database tenant
5. show pharmacy staff login
6. open permission-filtered pharmacy shell

Verification:
- dotnet restore: passed
- Release build: passed
- warnings: 0
- errors: 0
- unit tests: 12 passed, 0 failed
- integration tests: 6 passed, 0 failed
- integration coverage includes:
  - real file-backed SQLite migration
  - migration-history table creation
  - WAL mode
  - tenant binding and cross-tenant rejection
  - safe local setting round trip
  - secret-setting rejection
  - 20 concurrent atomic sequence allocations
- EF Core has-pending-model-changes: no pending changes
- NuGet vulnerability audit: no vulnerable packages reported
- verification environment: Ubuntu 24.04 / .NET SDK 10.0.401 / net10.0-windows cross-build

GitHub Actions note:
- branch workflow run 36626539896 failed before exposing workflow steps
- GitHub returned steps: null and the redirected job log blob returned BlobNotFound
- this is not recorded as an application test failure because the same commit was independently restored, built and tested successfully
- the runner anomaly should continue to be monitored, but it does not replace the explicit verification above

Known follow-ups:
- native Windows startup smoke testing remains required before release
- module-specific entities/indexes/repositories will be added in their scheduled batches
- backup/restore is intentionally not implemented until Batch 19
- sync outbox/conflict/cursor schema is intentionally not implemented until Batch 18

Next batch:
- Batch 8 — Dashboard.
