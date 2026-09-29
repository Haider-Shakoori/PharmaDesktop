# Desktop Architecture — Batch 1

## Responsibility split

Laravel SaaS remains authoritative for tenant existence, provisioning, trial eligibility, subscription state, license/device state, plan limits, and cloud-side identity. The Windows client is the operational pharmacy client and is authoritative only for immediate local offline operations until synchronization succeeds.

## Solution layers

Presentation: BusinessOS.Pharmacy.Desktop (WPF/MVVM)

Application: use cases and interfaces; no WPF or EF dependencies.

Domain: business types and invariants.

Infrastructure: operating-system and external-service adapters.

Persistence: EF Core + SQLite implementation.

Licensing: activation, signed lease verification, secure entitlement storage.

Sync: idempotent push/pull queue, retry, conflict policy.

Printing: receipt/report printing adapters.

Updater: version manifest and verified update flow.

Dependencies point inward toward Application/Domain. Business rules must not be embedded directly in WPF views.

## Local storage

Default business-data root is C:\ProgramData\BusinessOS\Pharmacy. Database, logs, and backups remain separate. Secrets and activation material will use DPAPI/Credential Manager rather than plaintext configuration.

## Startup target

Normal startup should load the local database and cached signed entitlement first, display the application promptly, and perform non-blocking licensing/sync checks afterward. Initial activation remains online-only.

## Data strategy

SQLite is the initial single-workstation store. Domain/application contracts must remain storage-agnostic so a later multi-terminal topology can add a local service/database without rewriting core pharmacy rules.

## Security baseline

Never embed a signing private key. Never trust local time alone for trial/subscription validity. Never trust tenant IDs supplied by an unauthenticated client. Never destroy local pharmacy data on license expiry.
