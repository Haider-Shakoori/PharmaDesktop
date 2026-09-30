# Darmaltoon LAN Architecture

## Status and sequencing

This LAN architecture was intentionally started early, immediately after Batch 9. Batches 10–17 are not yet implemented on `main`.

Therefore this document describes the LAN foundation that exists now and the authoritative architecture that later inventory, purchase, customer, POS, returns, expenses, daily-closing and reporting modules must plug into.

Batch 18 must not be marked complete until the Batch 10–17 transaction modules exist and their multi-terminal acceptance tests have actually passed.

## Deployment modes

Darmaltoon uses the strongly typed `DeploymentMode` enum:

- `Standalone`
- `Server`
- `Client`

### Standalone

The desktop application uses the existing in-process Application services and authoritative local SQLite database directly.

No HTTP hop is introduced for normal standalone use.

### Main Pharmacy Server

The Main PC owns:

- the activated SaaS tenant/license
- the authoritative local database
- the `BusinessOS.Pharmacy.LocalServer` Windows-service host
- terminal registry and pairing state
- LAN pharmacy-user sessions
- future authoritative cloud synchronization

The desktop UI on the Main PC still uses the in-process Application services directly. The Local Server exposes those same Application contracts to paired terminals.

### Client Terminal

Client mode does not register the authoritative SQLite persistence services and does not initialize `pharmacy.db`.

The Windows UI resolves LAN-backed Application-service implementations from `BusinessOS.Pharmacy.LocalClient`.

A client may retain only:

- non-secret network configuration
- DPAPI-protected terminal credentials
- UI/session state
- safe reference/cache data where explicitly designed

A client must not create an independent transaction history when disconnected from the Main Server.

## Data ownership

### Shared authoritative data

Owned by Standalone/Main Server:

- pharmacy identity
- medicine master
- future inventory/batches
- future purchasing
- future customers
- future sales/returns
- future expenses/payments
- future daily closing
- future reports/accounting state

### Terminal-local settings

Remain local to the Windows terminal where appropriate:

- workstation name
- default printer
- receipt width
- window/UI preferences
- paired-server address hints
- DPAPI-protected terminal credentials

## Database rule

Client computers never open the server SQLite file through SMB, mapped drives, UNC paths, shared folders or network filesystems.

The authoritative SQLite file is opened only on the machine that owns it.

Client access is through the versioned LAN API.

## Projects

### BusinessOS.Pharmacy.LocalServer

ASP.NET Core/Kestrel host with Windows Service support.

Responsibilities:

- TLS endpoint
- `/api/local/v1` API
- server identity
- terminal authentication
- pharmacy-user LAN login/session authentication
- role/permission enforcement
- rate limiting
- medicine/dashboard adapters already available
- UDP discovery responder
- structured network logging

It uses existing Application/Persistence services rather than duplicating business rules.

### BusinessOS.Pharmacy.LocalClient

Responsibilities:

- TLS certificate pinning
- terminal credential headers
- pharmacy-user bearer session
- LAN-backed medicine/dashboard Application-service adapters
- terminal pairing client
- automatic connection health monitor and reconnect backoff

### Application

Owns deployment/network contracts and remains independent of SQLite/Kestrel.

### Persistence

Owns authoritative server metadata:

- server identity
- registered terminals
- one-time pairing codes
- network audit records
- cached verified LAN users
- LAN user sessions

All schema changes are additive EF Core migrations.

## Current LAN API

Base path:

`/api/local/v1`

Current foundation endpoints include:

- `GET /health`
- `GET /server-info`
- `POST /pairing/complete`
- `POST /auth/login`
- `POST /terminals/heartbeat`
- `GET /medicines`
- `GET /medicines/references`
- `GET /medicines/{id}`
- `POST /medicines`
- `PUT /medicines/{id}`
- `POST /medicine-categories`
- `POST /manufacturers`
- `GET /dashboard`

Inventory, purchase, customer, sales, returns, expenses, daily closing and report endpoints are intentionally not fabricated before Batches 10–17 exist.

## Security layers

1. TLS with server certificate pinning
2. persistent Terminal UUID
3. high-entropy terminal secret
4. terminal revocation check on every protected request
5. pharmacy-user login and short-lived local session token
6. user role/permission check
7. optional terminal-specific permission restriction
8. rate limiting
9. request validation
10. network audit logging

LAN is never treated as trusted merely because it is local.

## Pairing

A pairing code is:

- six digits
- short lived
- one time
- attempt limited
- hashed server-side
- never persisted on the client after pairing

Successful pairing returns a random terminal secret.

The server stores only a hash of that secret.

The client stores the terminal secret only in the protected network secret store.

## Server identity and TLS

Each server has a persistent Server UUID bound to one tenant.

Clients remember:

- Server UUID
- tenant ID
- TLS certificate SHA-256 fingerprint
- Terminal UUID

Rediscovery accepts a server only when Server UUID and certificate fingerprint still match.

Certificate validation is not globally disabled.

## Discovery

UDP discovery exposes only safe identification metadata:

- service/API identity
- server UUID
- server display name
- hostname
- port
- certificate fingerprint

It does not broadcast subscription keys, owner details, customers, revenue, credentials or database paths.

Manual hostname/IP configuration remains available.

## Connectivity separation

Darmaltoon treats these as separate concepts:

- LAN Server status
- cloud/internet status
- license status
- sync status

An internet outage must not be treated as a LAN outage.

In Server mode, local operations remain possible while the valid offline entitlement permits them.

## Automatic reconnection

Client mode includes a background connection monitor.

Retry cadence after failures:

- 2 seconds
- 5 seconds
- 10 seconds
- 30 seconds maximum

When healthy, the monitor checks approximately every five seconds.

The transport keeps a persistent `HttpClient` and TLS pin.

Disconnected clients do not create competing authoritative transactions.

## Private-network exposure

The local server uses a configurable port, default `5280`.

Windows Firewall rules are restricted to the Private profile.

The Local Server process refuses to start on a Public-only Windows network.

The Network & Terminals screen reports the current Windows network profile and firewall status.

## Mode changes

### Standalone -> Server

Allowed safely.

The existing tenant activation and existing authoritative database are preserved. The same database becomes the Main Server database.

### Server -> Standalone

Allowed without deleting data. The database remains local to the same machine.

### Client -> Server / Server -> Client / Standalone -> Client from Settings

Not silently promoted.

These change data ownership and require explicit migration/recovery/pairing workflows.

Darmaltoon does not automatically delete, copy or promote client data.

## Cloud synchronization ownership

Future cloud synchronization must follow:

`Clients -> Main Server -> BusinessOS Cloud`

Client terminals do not independently push the same authoritative transaction stream to Laravel.

## Future transaction requirements

When Batches 10–17 are implemented, their Application services must ensure:

- inventory validation inside server transactions
- atomic sale commit
- authoritative invoice/reference allocation on server
- terminal/user/session audit fields
- unified daily closing across all terminals
- terminal-aware cash/drawer breakdown where required
- no duplicate financial logic in LAN controllers

## Version compatibility

The LAN API is versioned as `v1`.

Server-info exposes API/application compatibility information.

Before release, the final upgrade policy must enforce a supported minimum client version.

Preferred rolling update order:

1. Main Server update
2. server database migration
3. API compatibility check
4. client updates

## Performance tradeoff

Standalone mode intentionally remains direct/in-process.

Server desktop mode intentionally remains direct/in-process.

Only Client Terminal mode pays HTTP/TLS serialization overhead.

This preserves the fastest path for single-PC pharmacies while providing controlled LAN centralization for multi-PC pharmacies.
