# Batch 18 LAN Acceptance Status

## Current status

**FOUNDATION IMPLEMENTED EARLY — NOT FINAL BATCH 18 ACCEPTANCE**

The project deliberately jumped from completed Batch 9 to Batch 18.

Batches 10–17 are still absent from `main` and therefore the following operational modules do not yet exist in native desktop form:

- inventory / product batches / expiry
- purchasing / suppliers
- customers
- POS / sales
- returns
- expenses / payments
- daily closing
- reports

Batch 18 must not be marked complete until those modules exist and their server-authoritative LAN behavior is tested.

## Foundation implemented

- strongly typed Standalone / Server / Client modes
- legacy-install preservation as Standalone
- first-run deployment selection
- deployment-aware DI
- Client mode without authoritative SQLite registration
- dedicated `BusinessOS.Pharmacy.LocalServer`
- dedicated `BusinessOS.Pharmacy.LocalClient`
- ASP.NET Core/Kestrel HTTPS LAN API
- Windows Service support
- technician install/uninstall scripts with automatic startup
- configurable API port (default 5280)
- configurable discovery port (default 5281)
- UDP server discovery
- manual hostname/IP fallback
- persistent Server UUID
- TLS certificate generation and client certificate pinning
- secure terminal registration/pairing
- short-lived one-time pairing codes
- random terminal secrets; hash stored server-side
- DPAPI-protected client network secrets
- terminal rename/revocation/last-seen
- LAN pharmacy-user login
- offline cached pharmacy-user login under existing entitlement limits
- user + terminal permission enforcement
- local LAN session tokens bound to terminal
- rate limiting
- network audit metadata/logging
- medicine LAN API/client adapter
- dashboard LAN API/client adapter
- automatic client connection monitor/retry backoff
- live shell LAN status badge
- Network & Terminals administration page
- settings.manage gate
- diagnostics report
- Public-network fail-closed server startup
- Private-only API TCP firewall rule
- Private-only discovery UDP firewall rule
- safe Standalone <-> Server mode conversion
- unsafe ownership mode changes blocked
- LAN architecture/setup/security/recovery documentation

## Automated verification already performed

### Build

Release solution build:

- 0 warnings
- 0 errors

Projects compiled include:

- Darmaltoon Desktop
- Application
- Domain
- Infrastructure
- Persistence
- Licensing
- LocalServer
- LocalClient
- Sync
- Printing
- Updater
- tests

### Unit tests

- 12 passed
- 0 failed

### Integration tests

- 21 passed
- 0 failed

LAN-specific coverage includes:

- non-secret network configuration
- tenant-bound persistent Server UUID
- one-time terminal pairing
- pairing replay rejection
- expired pairing rejection
- terminal secret authentication
- wrong terminal secret rejection
- terminal revocation
- Client service graph has no authoritative DB initializer
- LAN user token bound to Terminal UUID
- tampered LAN user token rejection
- wrong-terminal session token rejection
- LAN user session expiry
- LAN user session revocation
- invalid Client configuration rejection

### Dependency audit

Current solution package graph reported no vulnerable packages from configured NuGet sources.

### EF Core model

The LAN metadata migrations are additive.

A previous current-branch EF check reported no pending model changes after LAN migrations.

## Verification environment limitation

Most automated compilation/tests were run on the authorized Linux verification VPS using .NET Windows cross-targeting where applicable.

This verifies managed logic and cross-build compatibility, but it cannot replace real Windows acceptance for:

- Windows Service installation/start
- DPAPI behavior across actual Windows machines/accounts
- Windows Private/Public profile detection
- Windows Firewall rules
- certificate trust/pinning between two Windows PCs
- UDP broadcast behavior through a real pharmacy router
- printer/device behavior

## Real multi-PC acceptance not yet performed

The required manual scenario has **not** been claimed as passed:

1. Main Server with stock 100
2. Client 1 sells 10
3. Client 2 sees 90 and sells 15
4. Main Server shows 75
5. both users and Terminal IDs visible
6. disconnect internet and repeat sale
7. restore internet and later sync

Reason: native Batch 10–17 inventory/POS/closing modules have not yet been implemented.

## Concurrency/load acceptance deferred

The following are explicitly deferred until Batches 10–17:

- two simultaneous cashier sale commits
- five-terminal load
- atomic stock decrement
- oversell prevention
- unique invoice allocation
- purchase entry concurrency
- customer lookup under load
- return/payment consistency
- daily closing across all terminals
- terminal drawer/cash breakdown
- report consistency under concurrent writes

No placeholder transaction logic will be invented merely to make these tests appear green.

## Network failure acceptance

### Implemented in architecture

- internet/cloud state is separate from LAN state
- Client reconnection monitor uses controlled backoff
- server rediscovery validates Server UUID + certificate fingerprint
- Client cannot create independent authoritative writes while disconnected

### Still requires physical Windows/LAN test

- router interruption
- Main Server restart
- DHCP/IP change
- Client reboot
- discovery across real switch/router/Wi-Fi
- real latency measurement

## Required continuation order

Because of the intentional jump:

1. preserve this Batch 18 branch/foundation
2. return to Batch 10
3. implement Batches 10–17 normally
4. integrate each new Application service into LocalServer/LocalClient without duplicating rules
5. return to Batch 18 acceptance
6. run concurrency, failure, security and manual multi-PC tests
7. only then declare Batch 18 complete
8. proceed to Cloud Synchronization Engine

## Merge/completion rule

This branch should not be described as a fully accepted Batch 18 release while the Batch 10–17 dependencies remain absent.

A draft/foundation PR is appropriate; a final Batch 18 completion report is not yet appropriate.
