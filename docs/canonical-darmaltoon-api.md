# Canonical Darmaltoon API (Windows)

Future installers must use `https://darmaltoon.com` for all cloud licensing and sync requests. `src/BusinessOS.Pharmacy.Desktop/appsettings.json` controls licensing `BaseUrl`, update manifest `ManifestUrl`, and sync `BaseUrl`. LocalServer fallback config uses the same origin. Keep existing Ed25519 license verifier and update-manifest RSA public keys unchanged.

| Component | Path |
| --- | --- |
| Activation | `/api/v1/license/activate` |
| License refresh | `/api/v1/desktop/license/refresh` |
| Session login/refresh | `/api/v1/desktop/session/{login,refresh}` |
| Desktop sync | `/api/v1/desktop/sync/{status,push,pull/...}` |
| Update manifest | `/api/v1/desktop/update/manifest` |

Clients persist local data and must work offline as permitted by their license. Do not erase local Windows activation keys, existing sync cursors, or tenant profiles during a host change. Previously installed clients with `https://pharmacy.darmaltoon.com` are supported temporarily on the legacy API origin. Changing packaged defaults alone does not overwrite user-specific endpoint settings already persisted on their machines.

**Do not build final Windows installers yet**. The final release phase must run activation/refresh, online and offline sync, update-signature verification, and a clean upgrade from an existing installation against the new canonical hostname; then build and distribute fresh installers. Do not change signing key material just because the hostname changed.
