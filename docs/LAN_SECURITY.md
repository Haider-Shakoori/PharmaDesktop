# Darmaltoon LAN Security

## Threat model

The pharmacy LAN is not trusted by default.

Possible threats include:

- unknown device on the same Wi-Fi
- stolen/replayed pairing code
- stolen terminal token
- revoked terminal
- malicious/expired pharmacy user session
- server hostname/IP reassignment
- TLS interception
- malformed requests
- permission bypass attempts
- Public-network exposure
- log/secret leakage

## TLS

The Main Server uses HTTPS.

The client pins the expected server certificate SHA-256 fingerprint established during secure pairing.

Darmaltoon does not use `DangerousAcceptAnyServerCertificateValidator` and does not globally disable certificate validation.

A hostname/IP resolving to a different certificate is rejected.

## Server identity

Each Main Server has a persistent Server UUID bound to the pharmacy tenant.

A server identity cannot silently rebind to another tenant.

Clients remember Server UUID + certificate fingerprint.

## Terminal identity

Each client has a persistent Terminal UUID.

Successful pairing issues a random 256-bit terminal secret.

The server stores only a SHA-256 hash of the terminal secret.

The client stores the secret only in protected network-secret storage.

No terminal secret belongs in `network.json`.

## Pairing codes

Pairing codes are:

- six digits
- short lived
- one time
- attempt limited
- salted/hashed server-side
- rate limited at the API
- invalid after successful use

Wrong, expired and replayed codes are rejected.

Pairing codes are not durable credentials.

## Pharmacy-user authentication

Terminal authentication is not sufficient to access pharmacy data.

Users also sign in with a pharmacy account.

The Main Server validates the user against the existing SaaS desktop-session architecture when internet is available.

For offline LAN login, the Main Server can use a previously verified cached user while:

- tenant still matches
- user is active
- cached identity validity has not expired
- Main Server entitlement remains valid
- password verifier matches

Passwords are not stored plaintext.

## LAN user sessions

Local LAN user sessions:

- use random 256-bit tokens
- store only token hashes server-side
- bind to one Terminal UUID
- carry tenant/user/roles/permissions
- expire
- can be revoked
- are rejected when presented from another terminal

The LAN session does not extend a trial or subscription.

## Authorization

Server-side `IPermissionAuthorizer` combines:

1. pharmacy-user permissions
2. optional terminal permission restrictions

The server remains the authority even if the UI hides a button.

Network & Terminals desktop administration requires `settings.manage`.

## Terminal revocation

A revoked terminal fails terminal authentication before pharmacy-user session authorization.

Old LAN session tokens therefore cannot bypass terminal revocation.

## Rate limiting

The Local Server applies rate limits to the versioned LAN API.

Pairing uses a stricter limiter than ordinary authenticated LAN traffic.

## Public network protection

Windows Firewall rules are created only for the Private profile.

Darmaltoon refuses to configure the inbound rule on a Public-only network.

The Local Server process also refuses to start on a Public-only Windows network.

## Discovery privacy

Discovery broadcasts only safe server-identification metadata.

Never broadcast:

- owner email
- subscription/license key
- terminal secret
- user token
- password
- customer/patient data
- revenue
- database path
- financial details

## Logging

Network logs may include:

- service startup/shutdown
- terminal ID
- connection/revocation events
- pairing outcome
- authentication failure
- API/network errors

Do not log:

- user passwords
- pairing code plaintext
- terminal secret
- bearer session token
- activation/license key
- certificate private key

## Secret recovery

DPAPI-protected client/server machine secrets should not be blindly copied to a replacement machine.

Recovery should restore safe configuration and authoritative data, then deliberately recreate/re-pair machine-bound credentials.

## Subscription enforcement

The Main Server's verified SaaS entitlement remains authoritative.

Client terminals do not create independent trial timelines.

Terminal-count entitlements must eventually be supplied by the SaaS plan/feature model; this LAN branch does not hardcode a terminal limit.

## Known security acceptance items still pending

Real Windows acceptance testing is still required for:

- Windows Service installation/startup
- DPAPI on separate Windows accounts/machines
- actual Private/Public network profile transitions
- Windows Firewall rule behavior
- real certificate pinning across two PCs
- UDP discovery across a pharmacy router
- terminal revocation against a running client
- server restart/reconnect

Transaction-security tests involving inventory/sales/closing are deferred because Batches 10–17 were skipped.
