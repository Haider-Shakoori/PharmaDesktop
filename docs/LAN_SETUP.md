# Darmaltoon LAN Setup Guide

## Important sequencing note

This guide covers the Batch 18 LAN foundation being developed early after Batch 9.

Medicine master and dashboard LAN access are available in the current branch.

Inventory, purchases, customers, POS, returns, expenses, daily closing and reports will be attached after their own Batches 10–17 are implemented.

## Choosing a mode

On a new installation Darmaltoon asks:

### Standalone Pharmacy

Choose this when one PC runs the pharmacy.

The PC stores the authoritative pharmacy database locally.

### Main Pharmacy Server

Choose this for the PC that will:

- store the authoritative pharmacy database
- host the local LAN service
- pair cashier/pharmacist/manager computers
- later own cloud synchronization

### Client Terminal

Choose this on every additional pharmacy PC.

A Client Terminal does not create a new pharmacy or a separate seven-day trial.

It pairs with the already activated Main Pharmacy Server.

## Existing installation upgrade

Existing installations from earlier batches are automatically preserved as Standalone when no network configuration exists but a local activation/database already exists.

This prevents a first-run LAN wizard from disrupting an existing pharmacy.

## Standalone -> Main Pharmacy Server

1. Sign in as an administrator with `settings.manage`.
2. Open **Network & Terminals**.
3. Change deployment mode from Standalone to Server.
4. Keep the desired server name and LAN port.
5. Restart Darmaltoon.
6. Run Diagnostics.
7. Confirm Windows network is Private/Domain.
8. Configure the Private Firewall rule.
9. Start the Darmaltoon Local Server Windows Service.
10. Create a five-minute pairing code when adding a terminal.

The existing database remains in place. Medicines and other authoritative data are not recreated.

## Main Server network requirements

Recommended:

- Windows network profile: Private
- server hostname: stable Windows computer name such as `PHARMACY-SERVER`
- default LAN API port: `5280`
- discovery port: `5281`

Prefer hostname/discovery over permanently relying on a DHCP IP address.

## Public network warning

Darmaltoon does not intentionally expose the Local Server on a Public-only Windows network.

If diagnostics show Public:

1. verify you are connected to the pharmacy's trusted router/LAN
2. change the Windows network profile to Private
3. rerun diagnostics
4. configure the Private Firewall rule
5. start the Local Server again

Do not create an Any/Public inbound firewall rule for the pharmacy server.

## Pairing a Client Terminal

On the Main Server:

1. Open **Network & Terminals**.
2. Choose **Create 5-minute pairing code**.
3. Keep the pairing screen open.
4. Give the code only to the intended terminal.

On the Client PC:

1. Install Darmaltoon.
2. Choose **Client Terminal**.
3. Use **Find Pharmacy Servers** or enter the hostname/IP manually.
4. Select/enter the exact Server UUID and certificate fingerprint.
5. Enter the short-lived pairing code.
6. Enter a useful terminal name such as `Front Counter 1`.
7. Finish pairing.
8. Sign in with the pharmacy user's normal account.

The pairing code is not reused.

## Manual connection fallback

If discovery does not find the server, enter:

- Server host: for example `PHARMACY-SERVER`
- Port: normally `5280`
- Server UUID
- TLS SHA-256 fingerprint
- short-lived pairing code

The server UUID/fingerprint must come from the trusted Main Server.

Do not trust a different server only because it answers at the same IP.

## Client restart

A paired Client Terminal stores:

- non-secret server configuration in `network.json`
- terminal secret in DPAPI-protected storage

After Windows restart it reconnects to the same paired server identity.

## Server IP change

Use **Find Server Again**.

Rediscovery accepts the server only if:

- Server UUID matches
- certificate fingerprint matches

This allows DHCP IP changes without silently trusting a replacement machine.

## Temporary LAN loss

The client displays the server as unavailable and reconnects automatically.

The retry backoff grows to a maximum of 30 seconds.

Do not perform disconnected authoritative sales on the client in this architecture.

## Internet loss while LAN remains connected

Normal local-server operations are designed to continue under the existing offline-license rules.

The client talks to the Main Server, not directly to the cloud for authoritative transactions.

Cloud synchronization resumes from the Main Server when internet returns.

## Managing terminals

Authorized administrators can use **Network & Terminals** to:

- see registered terminals
- see computer/role/last-seen state
- rename a terminal
- revoke a terminal
- create a new pairing code
- test connection
- rediscover the paired server
- inspect network/firewall diagnostics

A revoked terminal is rejected by terminal authentication even if it still holds an old local session token.

## Network configuration files

Non-secret configuration:

`C:\ProgramData\BusinessOS\Pharmacy\Config\network.json`

Protected secrets:

`C:\ProgramData\BusinessOS\Pharmacy\Config\network-secrets.bin`

Do not copy the protected secret file to another Windows machine and assume it will decrypt there.

## Diagnostics

Open **Network & Terminals -> Run Diagnostics**.

The report includes safe information such as:

- deployment mode
- Local Server service state
- LAN connection state
- Windows network profile
- firewall status
- host/port
- authoritative database ownership
- disk free space
- license state

It intentionally does not display passwords, terminal secrets, tokens, license keys or private certificate material.

## Current acceptance limitation

Because the project jumped from Batch 9 directly to Batch 18, this setup has not yet been accepted for live multi-terminal POS/inventory operations.

The final two-cashier/five-terminal sale/stock/closing acceptance test must be performed only after Batches 10–17 exist.
