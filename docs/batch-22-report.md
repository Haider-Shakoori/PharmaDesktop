# Batch 22 — Laravel / SaaS Integration

Darmaltoon now integrates its existing signed Windows licensing/session flow with a desktop-specific BusinessOS cloud synchronization boundary while preserving local-first pharmacy operation.

## Licensing and tenant identity

- Laravel remains authoritative for tenant, pharmacy, subscription, trial, license, activation, plan limits and subscription health.
- Windows activation continues through the existing signed offline lease flow; plaintext license keys are never persisted locally.
- Desktop access sessions are signed by Laravel and bound to tenant, activation, Windows device and pharmacy user.
- The protected desktop session store uses matching Windows DPAPI CurrentUser protection/unprotection scopes.
- Local SQLite initialization still refuses a signed activation for a different tenant.

## Desktop cloud synchronization

- New SaaS endpoints: `/api/v1/desktop/sync/push` and `/api/v1/desktop/sync/pull/{stream}`.
- Endpoints accept only signed `desktop_access` tokens, never offline activation leases.
- Every request revalidates the active Windows activation, license, subscription health, tenant and pharmacy user on Laravel.
- SaaS synchronization reuses the existing tenant-scoped sync engines for idempotent sales and incremental medicine/customer/inventory streams.
- Desktop sales are added to a local cloud outbox in the same SQLite transaction as the sale and stock movements.
- Outbox records contain tenant and cashier identity; one cashier cannot upload another cashier's pending sale under the wrong SaaS identity.
- Accepted server acknowledgements are idempotent. Retryable transport/server failures remain pending with exponential retry. Non-retryable business conflicts are retained locally for review and never delete or rewrite the local sale.
- Pull cursors and downloaded records are tenant-scoped and stored as isolated cloud snapshots. They do not overwrite authoritative local operational inventory.

## Deployment modes

- **Standalone:** owns the local database and synchronizes with BusinessOS when a valid desktop user session and network are available.
- **Main Pharmacy Server:** owns the shared LAN database and is the only machine in a multi-terminal pharmacy that synchronizes operational data with BusinessOS.
- **Client Terminal:** cloud synchronization is disabled; it reads/writes through the authenticated Main Server LAN API, preventing duplicate cloud writers.

## Offline behavior

Internet loss never blocks or rolls back a committed local pharmacy transaction. The sync worker is best-effort, runs in the background, and catches transport failures. Signed offline lease/session expiry rules remain unchanged; when connectivity returns Laravel revalidates current subscription state before accepting synchronization.

## Security

- HTTPS is required for cloud sync configuration.
- Access tokens are sent only as Bearer headers and remain DPAPI-protected at rest.
- Tenant, activation, device and user identities are compared before cloud transport.
- Laravel switches to the resolved tenant context before reading or writing tenant records.
- Subscription cancellation/expiry blocks online synchronization.
- Pull data, cursors and outbox rows are tenant-keyed locally.
