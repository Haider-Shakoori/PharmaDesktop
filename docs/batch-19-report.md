# Batch 19 — Backup & Restore

Implemented native backup and restore for the authoritative local pharmacy database.

## Safety model

- Backup/restore is available only on Standalone or Main Pharmacy Server installations.
- Client Terminals never back up or replace a local authoritative database.
- Backups use SQLite online backup semantics, so WAL-backed committed data is included consistently.
- Every Darmaltoon backup receives a SHA-256 manifest with tenant ID, database instance ID and latest EF migration.
- Verification checks SQLite integrity, tenant identity and manifest checksum before restore.
- Cross-tenant, damaged, altered or manifest-less restore input is rejected before the live database is changed.
- Restore creates a verified pre-restore safety backup first.
- Main Server restore stops the LAN Windows Service before replacing the database and restarts it afterward.
- Restore removes stale WAL/SHM sidecars, applies normal forward EF migrations, then revalidates the restored tenant/database.
- If restore validation or migration fails, the previous live database is restored from the rollback copy.

## UI

Settings administrators receive a Backup & Restore workspace on Standalone and Main Server modes. Restore requires selecting a verified backup and typing `RESTORE` before the command is enabled.

## Automated coverage

Integration tests cover backup/restore round-trip, pre-restore safety backup, tamper rejection and tenant-mismatch rejection without changing live data.
