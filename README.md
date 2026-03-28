# Obsidian Sync Manager

A self-hosted management layer for [Obsidian LiveSync](https://github.com/vrtmrz/obsidian-livesync) built with .NET Aspire, Blazor, CouchDB, and generic OpenID Connect authentication.

## Container Image

The container image is published to GitHub Container Registry on every push to `main`:

```
ghcr.io/<owner>/obsidian-sync-manager:latest
```

### Running the Container

```bash
docker run -d \
  -p 8080:8080 \
  -e COUCHDB_URL=http://couchdb:5984 \
  -e COUCHDB_USERNAME=admin \
  -e COUCHDB_PASSWORD=secret \
  -e OIDC__Authority=https://idp.example.com/realms/obsidian-sync \
  -e OIDC__ClientId=obsidian-web \
  ghcr.io/<owner>/obsidian-sync-manager:latest
```

Replace `<owner>` with the GitHub user or organization that owns the repository.

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
    image: ghcr.io/<owner>/obsidian-sync-manager:latest
    ports:
      - "8080:8080"
    environment:
      COUCHDB_URL: http://couchdb:5984
      COUCHDB_USERNAME: admin
      COUCHDB_PASSWORD: secret
      OIDC__Authority: https://idp.example.com/realms/obsidian-sync
      OIDC__ClientId: obsidian-web
    depends_on:
      - couchdb

volumes:
  couchdb-data:
```

---

## Configuration

### CouchDB

| Variable | Description | Example |
|----------|-------------|---------|
| `COUCHDB_URL` | Base URL of the CouchDB HTTP API | `http://couchdb:5984` |
| `COUCHDB_USERNAME` | Admin username | `admin` |
| `COUCHDB_PASSWORD` | Admin password | `secret` |

On startup, the app automatically initializes CouchDB (single-node setup, CORS for Obsidian LiveSync clients, authentication enforcement, and size limits). See [DEVELOPER.md](DEVELOPER.md) for details.

### OIDC

| Variable | Description | Example |
|----------|-------------|---------|
| `OIDC__Authority` | Issuer / authority URL | `https://idp.example.com/realms/obsidian-sync` |
| `OIDC__ClientId` | OIDC client identifier | `obsidian-web` |

Your OIDC provider must:

- Support the **Authorization Code** flow with a public client (no client secret)
- Include a `groups` claim in ID tokens containing group names (`obsidian-admins`, `obsidian-users`)
- Allow the redirect URIs used by the web frontend

### Authorization Policies

| Policy | Required Group(s) |
|--------|-------------------|
| `Admin` | `obsidian-admins` |
| `User` | `obsidian-users` or `obsidian-admins` |

---

## Development

See [DEVELOPER.md](DEVELOPER.md) for local development setup using .NET Aspire.
