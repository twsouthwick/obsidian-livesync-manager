# Project Guidelines

## Conventions

- Root namespace: `Swick.Obsidian.SyncManager.Web` (sub-namespaces: `.CouchDb`, `.Authentication`, `.Components`)
- Use the C# 13 `extension` keyword for extension methods — not traditional `static class` syntax
- Service registration via `builder.AddXxx()` extension methods in `Program.cs` (e.g., `AddCouchDb()`, `AddApplicationAuthentication()`)
- Blazor pages use `@rendermode InteractiveServer` and `@attribute [Authorize]` with cascading `Task<AuthenticationState>`
- CouchDB database names: `livesync-{workspaceId}` — workspaceId is a 12-char GUID prefix without hyphens
- No `appsettings.json` overrides at runtime — all config via environment variables
- Per-user CouchDB passwords are derived from the OIDC `sub` claim via HMAC-SHA256; setup URIs use PBKDF2 + HKDF + AES-256-GCM encryption
- Conventional commits: `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`