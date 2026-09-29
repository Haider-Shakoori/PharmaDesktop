# Laravel Pharmacy Audit — Batch 1

Source of truth audited: Haider-Shakoori/Pharmacy main branch.

## Boundaries

The current Laravel application separates central SaaS data from tenant pharmacy data. Central models own tenants, businesses, plans, subscriptions, licenses, activations, trial requests, and provisioning. Tenant databases own users, roles, medicine master data, purchasing, batch inventory, POS sales, returns, shifts, Daily Closing, cash safe, accounting, and settings.

The C# desktop client must preserve this boundary. It must not recreate platform administration locally.

## Licensing and trials

The existing API is POST /api/v1/license/activate. The activation service already accepts platform=windows and enforces a separate plan.max_windows_devices limit.

License keys are generated server-side using cryptographically secure random bytes. The current default prefix is PHM, and only a SHA-256 hash is stored. Rotation increments license version and revokes existing activations.

Successful activation returns an Ed25519-signed v1 lease token plus tenant/plan metadata. The server private key stays in Laravel. Lease expiry is capped by the plan offline grace period and by the subscription/trial end date.

A trial is one-time and server-owned. The current TrialProvisioner requires an application-ready tenant, creates the TRIAL subscription, sets trial_started_at/trial_ends_at, marks business.trial_used_at, and generates the license. The default trial plan allows one Windows device.

## Important compatibility note

The Laravel repository contains docs/windows-offline-edition.md describing an earlier Flutter Windows client. The new BusinessOS Pharmacy Desktop direction supersedes Flutter only for the Windows client implementation. The proven server-side licensing, trial, device-limit, offline-lease, tenancy, and synchronization semantics remain authoritative unless intentionally changed.

## Gaps to resolve in later batches

The current signed lease payload includes IDs, license version, device, plan code, subscription status, issued_at, and expires_at. Plan features are returned beside the signed token, not inside it. Before feature entitlements are trusted offline, Batch 4/5 should either sign the feature set or add a signed entitlement envelope.

The existing public API is mobile-oriented in naming for sync endpoints. Desktop should reuse safe existing endpoints where contracts fit and add versioned desktop-specific endpoints only where necessary.

Purchase returns are required by the desktop master prompt, but no tenant purchase-return route/table was identified in the current audited Laravel implementation. Treat that as a parity gap, not as an existing rule to invent.
