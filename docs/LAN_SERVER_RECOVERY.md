# Darmaltoon Main Server Recovery

## Purpose

This procedure covers failure/replacement of the Main Pharmacy Server computer without creating a second authoritative pharmacy history.

It assumes the official backup system from the relevant backup batch is available.

Because Batch 18 was started early after Batch 9, final inventory/POS/closing recovery acceptance remains deferred until Batches 10–17 exist.

## Core recovery rule

At any moment, one pharmacy tenant must have one intended authoritative local Main Server database for LAN operations.

Do not start two restored servers from the same backup and let both accept live transactions.

## If the Main Server hardware fails

1. Stop using all Client Terminals for authoritative writes.
2. Preserve the failed machine/disk if possible.
3. Obtain the newest verified Darmaltoon authoritative backup.
4. Prepare a replacement Windows PC.
5. Install the same or a compatible Darmaltoon version.
6. Restore the authoritative database using the supported backup/restore workflow.
7. Activate/restore the existing pharmacy tenant according to the licensing recovery workflow.
8. Configure the replacement as Main Pharmacy Server.
9. Ensure the restored database tenant matches the activated tenant.
10. Establish a replacement server identity deliberately.
11. Configure Private network/firewall.
12. Start the Local Server.
13. Re-pair Client Terminals.
14. Verify pharmacy-user login and medicine/dashboard data.
15. Only then resume live operations.

## Server UUID handling

Do not casually copy or duplicate a server identity onto two live machines.

If the original machine is permanently retired, the recovery workflow may restore or replace the Server UUID according to the final recovery policy.

If a new Server UUID/certificate is created, clients must re-pair rather than silently trusting the new identity.

## TLS certificate handling

The server certificate contains private cryptographic material.

If the backup design deliberately protects/restores it, follow that procedure.

Otherwise generate a new local-server certificate on the replacement machine and re-pair terminals so the new fingerprint is trusted explicitly.

Never disable certificate validation to avoid re-pairing.

## DPAPI secrets

DPAPI machine-bound files may fail to decrypt on another PC.

Do not depend on copying:

- network secret files
- terminal credentials
- private certificate password blobs

from the failed machine.

Recovery must tolerate regeneration/re-pairing of machine-bound credentials.

## Client recovery after server replacement

For each Client Terminal:

1. open the deployment/network setup
2. find or manually enter the replacement Main Server
3. verify the new Server UUID/fingerprint from the trusted Main PC
4. create a fresh short-lived pairing code on the Main PC
5. pair the terminal
6. sign in with the pharmacy user
7. verify connection/latency

Do not accept a new certificate automatically merely because the hostname is unchanged.

## IP/hostname change without server replacement

If the Main Server identity and certificate are unchanged but DHCP changes the IP:

- use server discovery
- require matching Server UUID
- require matching certificate fingerprint
- update only the host/address hint

No new pharmacy/tenant/trial is created.

## Database integrity

Never use destructive recovery commands such as:

- database delete
- migrate fresh
- truncate
- manual replacement of selected financial tables

Restore the authoritative backup as one coherent data set, then apply normal forward EF migrations.

## Cloud synchronization after recovery

Only the recovered Main Server resumes authoritative cloud synchronization.

Clients must not independently upload transactions in an attempt to "catch up."

Before final production release, sync recovery must include idempotency/cursor validation so a restored server cannot duplicate already-synchronized cloud operations.

## Recovery verification checklist

Before reopening the pharmacy:

- correct tenant activation
- correct restored database
- EF migrations applied successfully
- Local Server service running
- Windows network is Private/Domain
- firewall rule correct
- server certificate valid
- disk space healthy
- terminals re-paired
- revoked terminals still revoked or intentionally re-created
- pharmacy-user permissions correct
- cloud state understood
- backup taken after successful recovery

## Current limitation

A true recovery drill with:

- stock
- simultaneous POS transactions
- payments
- returns
- expenses
- daily closing
- cloud-sync continuation

cannot be truthfully executed until Batches 10–17 and the later sync/backup implementations exist.
