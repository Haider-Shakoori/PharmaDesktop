# LAN Architecture Audit

## Audit date and repository state

Repository: `Haider-Shakoori/PharmaDesktop`

Audited branch: `main`

Audited main commit: `5caace89867ef95e7b0cc56ba507e60fbf6a2a2f`

The repository currently contains completed batch reports only through **Batch 9**. Batches 10–17 are not present on `main` at the time of this audit.

Therefore the current native operational scope is:

- licensing / activation
- signed pharmacy-user session and permissions
- WPF shell
- local SQLite/EF Core persistence
- dashboard foundation
- medicine/category/manufacturer master data
- CSV medicine import

The following Batch 18 dependencies do **not** yet exist in this repository:

- authoritative inventory/batch/expiry module
- purchasing/supplier module
- customer module
- POS/sales module
- sales returns
- expenses
- daily closing
- reports

Batch 18 must not invent duplicate placeholder financial or inventory logic merely to expose LAN endpoints. Those endpoints can be added when their corresponding application services exist.

## Existing assumptions that couple desktop process to database ownership

### Desktop host always installs local SQLite persistence

`DesktopHost.Build()` always calls:

- `AddBusinessOSInfrastructure(paths)`
- `AddBusinessOSPersistence()`

This means every desktop process currently receives a direct `PharmacyDbContext` factory and SQLite-backed application services.

This is correct for Standalone and Main Server modes, but unacceptable for Client Terminal mode.

### Startup always initializes the authoritative local database

`StartupCoordinator.StartAsync()` always:

1. validates/activates the SaaS entitlement
2. initializes the local SQLite database
3. seeds medicine master data
4. shows pharmacy staff login

A Client Terminal must not perform steps 1–3 as though it were an independent pharmacy database owner.

### Application interfaces exist, but persistence implementations are local-only

The current medicine UI depends on `IMedicineCatalogService`, which is good separation.

However the registered implementation is always `MedicineCatalogService`, which uses EF Core/SQLite directly.

Client Terminal mode needs an implementation of the same application contract backed by the local-server API instead of SQLite.

### Dashboard query is local SQLite-specific

`ILocalDashboardQueryService` is implemented by `LocalDashboardQueryService`.

Its name and implementation intentionally assume local DB ownership. A LAN-capable dashboard should eventually use a deployment-neutral dashboard application/query interface, with local and LAN implementations.

### User-session service is process-scoped/stateful

`PharmacyUserSessionService` maintains one `Current` user session and one protected desktop session state on the Windows machine.

That fits a single interactive desktop process.

It is **not** suitable as the authoritative multi-user local-server session manager for simultaneous terminals. The local server requires request/session-scoped user identity and terminal identity rather than a single mutable `Current` user.

### Local sequence service is database-owner specific

`ILocalSequenceService` allocates values directly in SQLite.

This is correct on Standalone/Server.

A Client Terminal must never allocate authoritative invoice/reference numbers independently. Later POS/purchase services must request authoritative numbers from the server transaction.

## Existing strengths that should be preserved

- Domain/Application projects do not directly reference SQLite.
- Medicine UI already depends on an Application abstraction.
- EF Core lives inside Persistence.
- licensing and signed user-session verification are separate from Persistence.
- local database is tenant-bound and fails closed on tenant mismatch.
- SQLite WAL/foreign-key/busy-timeout hardening already exists.
- DPAPI is already used for activation/session secrets.
- Standalone operation does not require HTTP for medicine master operations.

These are strong foundations for LAN mode and should not be rewritten for aesthetic reasons.

## Required architecture changes

### Deployment mode boundary

Add strongly typed:

`DeploymentMode.Standalone`
`DeploymentMode.Server`
`DeploymentMode.Client`

Persist non-secret network configuration under:

`C:\ProgramData\BusinessOS\Pharmacy\Config\network.json`

Do not put secrets in this file.

### Persistence ownership routing

Standalone:
- Desktop -> local Application services -> SQLite

Server:
- Desktop -> local Application services -> authoritative SQLite
- Local Server API -> same Application services -> same authoritative SQLite

Client:
- Desktop -> LAN-backed Application service implementations -> Main Server API
- no direct registration/initialization of authoritative SQLite business persistence

### Dedicated local server

Add:

`src/BusinessOS.Pharmacy.LocalServer/`

Use ASP.NET Core/Kestrel and the existing Application/Persistence layers.

Do not expose EF entities directly.

The server should ultimately run as a Windows Service in Server mode.

### Local client boundary

Add LAN HTTP clients in Infrastructure or a focused local-network project.

The WPF project must not contain raw HTTP serialization/business logic.

### Terminal security

Add persistent server UUID and terminal UUIDs.

Pairing must be explicit and short-lived.

Terminal secrets/tokens must use DPAPI-protected storage.

LAN discovery must expose only safe identification metadata.

### Multi-user request identity

The local server needs a request identity containing at least:

- tenant ID
- user ID
- terminal ID
- local session ID
- roles/permissions

Authoritative transaction services should receive this identity through an Application abstraction, not read WPF singleton state.

### Cloud sync ownership

Standalone: local desktop remains sync owner.

Server: Main Server is sync owner.

Client: must not independently push authoritative pharmacy transactions to Laravel.

## Affected projects

### BusinessOS.Pharmacy.Application
Add deployment/network contracts, terminal/request identity abstractions, and deployment-neutral service contracts as feature modules arrive.

### BusinessOS.Pharmacy.Infrastructure
Add:
- network configuration store
- DPAPI-protected terminal/server secret store
- discovery implementation
- LAN HTTP clients
- connectivity/status services

### BusinessOS.Pharmacy.Persistence
Add safe LAN metadata migrations for:
- server identity
- registered terminals
- pairing requests / replay protection
- terminal audit/session metadata where appropriate

Persistence remains replaceable; Domain/Application must not depend on SQLite.

### BusinessOS.Pharmacy.LocalServer
New justified project:
- Kestrel host
- versioned `/api/local/v1` API
- terminal authentication/pairing
- application-service adapters
- health/status endpoints
- rate limiting
- structured network logging

### BusinessOS.Pharmacy.Desktop
Add:
- first-run deployment selection
- Settings -> Network & Terminals
- diagnostics/status UX
- client-server connection state
- deployment-aware DI composition

Do not put authoritative business rules in WPF view models.

## Migration strategy

### Existing Standalone -> Server

Safe path:
1. preserve current SaaS activation
2. preserve current tenant-bound database
3. set deployment mode to Server
4. create/persist server UUID
5. start local-server service against the existing database
6. pair additional terminals

No medicine re-entry, new tenant or new trial is required.

### Client -> Server

Must not silently promote client caches into authoritative data.

A future explicit recovery/migration workflow must decide which authoritative backup/database is used.

### Server -> Client

Must not delete the existing server DB.

Require explicit backup/migration confirmation before ownership changes.

### Schema changes

Only additive/safe EF migrations.

Never:
- database delete
- EnsureDeleted
- migrate fresh
- drop/truncate customer operational data

## Compatibility risks

1. **Batches 10–17 are absent.**
   Full multi-terminal sales/stock/purchase/closing completion cannot be truthfully implemented or tested yet.

2. **Single-user session singleton.**
   Current `IUserSessionService.Current` cannot represent simultaneous terminal users on the server.

3. **Dashboard interface naming.**
   `ILocalDashboardQueryService` encodes local ownership and will need a deployment-neutral facade later.

4. **Client licensing flow.**
   Current startup expects every desktop installation to have its own activated lease. Client-terminal pairing must be introduced without creating a separate trial or bypassing SaaS terminal/device entitlements.

5. **SQLite concurrency must be measured once transactional modules exist.**
   WAL is suitable for small workloads, but authoritative simultaneous sale/stock tests cannot be performed until POS/inventory services exist.

6. **Windows-specific validation remains required.**
   Cross-build verification on Linux can validate compilation/tests, but Windows Service, Firewall, DPAPI, private-network detection and real multi-PC discovery require Windows acceptance tests.

## Batch 18 implementation rule

Proceed incrementally without rewriting working Standalone code.

The Batch 18 branch may build the LAN foundation now, but **must not be declared complete** until Batches 10–17 exist and the required multi-terminal sales/inventory/closing acceptance tests are actually executed.
