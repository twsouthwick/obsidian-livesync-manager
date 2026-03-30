# Obsidian Sync Manager

A self-hosted management layer for [Obsidian LiveSync](https://github.com/vrtmrz/obsidian-livesync) with CouchDB and generic OpenID Connect authentication.

## Features

- **Workspace management** — create, delete, and share LiveSync workspaces through a web UI
- **Member management** — invite other users to a workspace; per-database access control in CouchDB
- **Setup URI generation** — one-click encrypted setup URIs that the Obsidian LiveSync plugin can auto-import (HKDF + AES-256-GCM)
- **Automatic CouchDB initialization** — single-node setup, CORS, auth enforcement, and size limits configured on first startup
- **Generic OIDC authentication** — works with any standards-compliant OpenID Connect provider (Keycloak, Authentik, Entra ID, etc.)

## Container Image

Published to GitHub Container Registry on every push to `main`.

### Running the Container

```bash
docker run -d \
  -p 8080:8080 \
  -e COUCHDB__URL=http://couchdb:5984 \
  -e COUCHDB__EXTERNALURL=https://couchdb.example.com \
  -e COUCHDB__USERNAME=admin \
  -e COUCHDB__PASSWORD=secret \
  -e OIDC__Authority=https://idp.example.com/realms/obsidian-sync \
  -e OIDC__ClientId=obsidian-web \
  -e OIDC__ClientSecret=my-client-secret \
  ghcr.io/twsouthwick/obsidian-sync-manager:latest
```

### Docker Compose Example

```yaml
services:
  couchdb:
    image: couchdb:latest
    environment:
      COUCHDB_USER: admin
      COUCHDB_PASSWORD: secret
    ports:
      - "5984:5984"
    volumes:
      - couchdb-data:/opt/couchdb/data

  web:
    image: ghcr.io/twsouthwick/obsidian-sync-manager:latest
    ports:
      - "8080:8080"
    environment:
      COUCHDB__URL: http://couchdb:5984
      COUCHDB__EXTERNALURL: https://couchdb.example.com
      COUCHDB__USERNAME: admin
      COUCHDB__PASSWORD: secret
      OIDC__Authority: https://idp.example.com/realms/obsidian-sync
      OIDC__ClientId: obsidian-web
      OIDC__ClientSecret: my-client-secret
    depends_on:
      - couchdb

volumes:
  couchdb-data:
```

---

## Configuration

All configuration uses the `__` (double-underscore) separator for nested keys.

### CouchDB

| Variable | Description | Example |
|----------|-------------|---------|
| `COUCHDB__URL` | CouchDB HTTP API base URL | `http://couchdb:5984` |
| `COUCHDB__EXTERNALURL` | Public CouchDB URL for Obsidian clients (falls back to `COUCHDB__URL`) | `https://couchdb.example.com` |
| `COUCHDB__USERNAME` | Admin username | `admin` |
| `COUCHDB__PASSWORD` | Admin password | `secret` |

`COUCHDB__URL`, `COUCHDB__USERNAME`, and `COUCHDB__PASSWORD` are **required**. `COUCHDB__EXTERNALURL` is optional — set it when CouchDB is behind a reverse proxy or on a different public hostname.

### Reverse Proxy

If the application is behind a reverse proxy (e.g. Nginx, Caddy, Traefik), set the following environment variable so ASP.NET Core correctly handles `X-Forwarded-For`, `X-Forwarded-Proto`, and related headers:

| Variable | Description | Example |
|----------|-------------|---------|
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | Enable forwarded headers middleware | `true` |

### OIDC

| Variable | Description | Example |
|----------|-------------|---------|
| `OIDC__Authority` | Issuer / authority URL | `https://idp.example.com/realms/obsidian-sync` |
| `OIDC__ClientId` | OIDC client identifier | `obsidian-web` |
| `OIDC__ClientSecret` | OIDC client secret | `my-client-secret` |
| `OIDC__Groups__Admins` | Group name for admin access | `obsidian-admins` (default) |
| `OIDC__Groups__Users` | Group name for user access | `obsidian-users` (default) |

Your OIDC provider must:

- Support the **Authorization Code** flow with a confidential client
- Include a `groups` claim in ID tokens containing the configured group names

### Authorization Policies

| Policy | Required Group(s) |
|--------|-------------------|
| `Admin` | `OIDC__Groups__Admins` group |
| `User` | `OIDC__Groups__Users` or `OIDC__Groups__Admins` group |

### Data Protection

The HMAC secret and workspace E2EE passphrases are encrypted at rest using ASP.NET Core Data Protection. The key ring is stored in CouchDB. By default, the key ring XML is unencrypted — to encrypt it, provide a PKCS#12 certificate:

| Variable | Description | Example |
|----------|-------------|---------|
| `DataProtection__CertificatePath` | Path to a `.pfx` / `.p12` file | `/etc/dp-cert/keystore.p12` |
| `DataProtection__CertificatePassword` | Password for the PKCS#12 file | `my-cert-password` |

When set, Data Protection keys stored in CouchDB are encrypted with the certificate's public key. A CouchDB-only compromise cannot decrypt the key ring without the certificate's private key.

> **Rotation**: ASP.NET Core Data Protection can read keys encrypted with previous certificates as long as those key ring entries remain in CouchDB. New keys are encrypted with the current certificate. Restart the app after replacing the certificate file.

For Kubernetes deployments using cert-manager, see [docs/DEPLOY.md](docs/DEPLOY.md).

---

## Development

See [DEVELOPER.md](DEVELOPER.md) for local development setup.
