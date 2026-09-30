# Batch 18 LAN Acceptance Status

## Current status

**BATCH 18 CODE COMPLETE — VPS AUTOMATED ACCEPTANCE PASSED**

The original LAN foundation was reconciled with the completed Batch 17 state without replacing valid LAN work. Batches 10–17 are now present and the Client Terminal routes operational pharmacy modules to the Main Pharmacy Server instead of owning an independent authoritative database.

## Deployment modes

- Standalone
- Main Pharmacy Server
- Client Terminal

Standalone and Server use the authoritative local SQLite database. Client Terminal uses authenticated HTTPS LAN adapters and does not register the authoritative database initializer.

## Server-authoritative LAN modules

The Local Server API and Local Client adapters now cover:

- dashboard
- medicines and medicine CSV
- inventory, batches, expiry, opening stock, adjustments and status changes
- suppliers and purchasing
- customers
- POS sales and FEFO checkout
- sale returns and refunds
- expenses and accounting journals
- cashier shifts and daily closing
- pharmacy reports and CSV exports

Business rules remain in the existing Application/Persistence services. The LAN layer transports DTOs and does not duplicate inventory, financial, pricing or closing rules.

## Multi-user request identity

Authorized LAN requests are bound to both Terminal ID and LAN pharmacy-user session. Server transaction services receive the authenticated user through a request-aware `IUserSessionService`, while permission checks remain request-aware through `IPermissionAuthorizer`.

This preserves correct CreatedBy, cashier, audit and approval identity for simultaneous terminals.

## Security and network foundation preserved

- persistent Server UUID
- TLS certificate generation and SHA-256 pinning
- one-time terminal pairing codes
- random terminal secrets with server-side hashes
- DPAPI-protected client secrets
- terminal rename, revoke and last-seen tracking
- LAN pharmacy-user sessions bound to Terminal UUID
- rate limiting
- network audit logging
- UDP discovery with manual host fallback
- controlled client reconnect backoff
- Public-network fail-closed server startup
- Private-only TCP and UDP Windows Firewall rules
- Windows Service install and uninstall scripts
- Network and Terminals administration UI

## Automated acceptance on VPS

The Linux verification VPS can validate managed code, SQLite transaction behavior and Windows cross-targeting. The final Batch 18 verification includes:

- Release solution build
- unit tests
- full integration tests including LAN foundation tests
- client service graph verification for every operational module
- no authoritative database initializer in Client Terminal service graph
- terminal pairing, replay rejection, expiry and revocation
- terminal secret authentication
- LAN user session binding, expiry, tamper rejection and revocation
- atomic local sequence allocation under concurrent writers
- simultaneous stock allocation protection against negative stock
- POS, return, purchasing, accounting, daily-closing and report regression coverage
- EF Core pending-model check
- NuGet vulnerability audit

## Physical Windows acceptance

The repository code is complete for Batch 18, but a Linux VPS cannot physically validate Windows-only deployment behavior. Before production rollout, run a real pharmacy LAN smoke test on Windows PCs for:

1. install Main Server as Windows Service
2. pair at least two Client Terminals
3. sell from Client 1 and verify immediate stock on Client 2
4. perform a second sale and verify authoritative remaining stock on Main Server
5. verify both users and Terminal IDs in audit data
6. disconnect internet while keeping LAN active and repeat operations
7. restart Main Server and confirm Client reconnect
8. test DHCP or server IP change through discovery
9. verify Private/Public Windows network profile and firewall behavior
10. verify printer and other pharmacy device behavior

These are environment acceptance checks, not missing Batch 18 application modules.

## Completion rule

Batch 18 code is complete when this branch builds, all automated tests pass, EF reports no pending model changes, dependency audit is clean, and the branch is based on the completed Batch 17 state. Physical Windows multi-PC deployment remains a production rollout validation step.
