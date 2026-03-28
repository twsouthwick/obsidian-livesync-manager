# Developer Guide

Local development setup for the Obsidian Sync Manager.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling)
- A container runtime (Docker Desktop, Podman, etc.)

## Quick Start

```bash
dotnet aspire run
```

Aspire will start CouchDB, Keycloak, and the web frontend. Open the Aspire dashboard to see all resources and endpoints.

Sign in with one of the test users:

| Username | Password | Groups |
|----------|----------|--------|
| `alice` | `alice` | `obsidian-admins`, `obsidian-users` |
| `bob` | `bob` | `obsidian-users` |

## Architecture

The AppHost orchestrates three resources:

1. **CouchDB** — data store for Obsidian LiveSync replicas
2. **Keycloak** — OIDC identity provider with a pre-configured realm
3. **Web frontend** — Blazor server app (waits for both dependencies to be healthy)

### Project Structure

| Project | Purpose |
|---------|---------|
| `obsidian-sync-manager.AppHost` | Aspire orchestration — defines resources and wiring |
| `obsidian-sync-manager.Web` | Blazor web frontend |
| `obsidian-sync-manager.ServiceDefaults` | Shared Aspire service defaults |

## CouchDB

### Container Configuration

- **Image:** `couchdb:latest`
- **Port:** 5984 (HTTP API)
- **Volume:** `couchdb-data` → `/opt/couchdb/data`
- **Health check:** `GET /_up`
- **Credentials:** Aspire parameters `couchdb-username` / `couchdb-password`

### Automatic Initialization

On startup, `CouchDbInitializer` runs with retry logic (up to 10 attempts, 2s delay) and configures:

1. **Single-node cluster setup** — enables the node for standalone use
2. **Authentication enforcement** — requires valid credentials for all requests
3. **CORS** — allows Obsidian LiveSync origins: `app://obsidian.md`, `capacitor://localhost`, `http://localhost`
4. **Size limits** — max HTTP request size (4 GB), max document size (50 MB)

### Database Naming Convention

User databases follow the pattern `livesync-{userId}-{workspaceName}`, where `userId` comes from the OIDC `sub` claim. Each database is secured so only the owning user has member access.

## OIDC / Keycloak

### Realm Configuration

The AppHost imports the `obsidian-sync` realm from `src/obsidian-sync-manager.AppHost/Realms/obsidian-sync-realm.json`. It includes:

- **Client:** `obsidian-web` — public client, Authorization Code flow
- **Groups:** `obsidian-admins` and `obsidian-users` — mapped to a `groups` claim via a protocol mapper
- **Test users:** `alice` (admin + user) and `bob` (user only)

### Authentication Flow

The web app uses ASP.NET Core cookie + OpenID Connect:

1. Unauthenticated users are redirected to `/login`, which issues an OIDC challenge
2. After sign-in at the identity provider, the user is redirected back with an authorization code
3. The app exchanges the code for tokens, creates a cookie session, and preserves the `id_token` for logout
4. `POST /logout` signs out of both the local cookie and the OIDC provider

### Authorization Policies

| Policy | Required Group(s) |
|--------|-------------------|
| `Admin` | `obsidian-admins` |
| `User` | `obsidian-users` or `obsidian-admins` |

### Using a Different OIDC Provider

The app uses generic `AddOpenIdConnect()` — no Keycloak-specific packages. To swap providers, update the environment variables wired in `AppHost.cs`:

| Variable | Description |
|----------|-------------|
| `OIDC__Authority` | Issuer / authority URL |
| `OIDC__ClientId` | OIDC client identifier |

The provider must support the Authorization Code flow with a public client and include a `groups` claim in ID tokens.

## CI/CD

The GitHub Actions workflow (`.github/workflows/ci.yml`) runs on push/PR to `main`:

1. Restores, builds, and tests the solution
2. On push to `main`, publishes a container image to `ghcr.io` using `dotnet publish /t:PublishContainer`
3. Tags images with both the commit SHA and `latest`
