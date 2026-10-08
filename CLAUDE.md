# CLAUDE.md — knk-web-api

## Global project context
@../../docs/ai-agents/GLOBAL_AGENT_INSTRUCTIONS.md

Read `AGENTS.md` and the current `knk-workspace/docs/ACTIVE_SESSIONS.md`
before editing. The import above is Claude Code syntax for the nested
`knk-workspace/Repository/knk-web-api` layout. If it does not resolve, locate
the shared file in the workspace or open it from knk-workspace's current
default branch. Do not rely on a dated handoff without checking the current
branches, issue, plan and tracker.

## Repo-specific conventions (knk-web-api)

**Stack:** ASP.NET Core on .NET 8 (`TargetFramework: net8.0`), EF Core via
`Pomelo.EntityFrameworkCore.MySql` against MySQL. The `.csproj` also
references `Microsoft.EntityFrameworkCore.SqlServer`, but the only
configured connection string (`appsettings.json` →
`ConnectionStrings:MySqlDbConnection`) is MySQL — the SqlServer package
appears unused; worth confirming/removing in a cleanup pass rather than
assuming SQL Server is in play anywhere.

**Dev secrets (closed-alpha hardening, D9):** `appsettings*.json` hold no
secrets: `ConnectionStrings:MySqlDbConnection`, `Security:Jwt:Secret`,
`Security:PluginApiKey` and `Email:SmtpPassword` are empty in git. Set them once
per dev machine with user-secrets (the csproj has a `UserSecretsId`), from the
repo root:

```
dotnet user-secrets set "ConnectionStrings:MySqlDbConnection" "Server=localhost;Database=knightsandkings_dev_v2;User=<user>;Password=<password>;Allow User Variables=True"
dotnet user-secrets set "Security:Jwt:Secret" "<output of: openssl rand -base64 48>"
dotnet user-secrets set "Security:PluginApiKey" "<output of: openssl rand -hex 32, same as the plugin's api.auth.api-key>"
dotnet user-secrets set "Email:SmtpPassword" "<smtp app password, only with Email:Provider=Smtp>"
```

Environment variables (`ConnectionStrings__MySqlDbConnection`, `Security__Jwt__Secret`, …)
work too and win over user-secrets. In Development a missing JWT secret is
generated at startup with a warning (logins then end on every restart); a missing
connection string stops startup with the command above. Outside Development the
API refuses to start without a connection string or with a JWT secret that is
empty, shorter than 32 characters or one that was ever committed
(`Configuration/StartupSecurity.cs`). Production settings go in an env file; see
the closed-alpha plan § 7 in knk-workspace for every key.

**Common commands:**
- Restore: `dotnet restore`
- Run: `dotnet run` (single Web SDK project at the repo root,
  `knkwebapi_v2.csproj`; the test project under `Tests/` is excluded from
  the main build via `DefaultItemExcludes`)
- Build: `dotnet build`
- Test: `dotnet test Tests/knkwebapi_v2.Tests/knkwebapi_v2.Tests.csproj`
- Add migration: `dotnet ef migrations add <Name>`
- Apply migrations: `dotnet ef database update`
- Local dev helper script: `./run-with-swagger.sh`

**Structure:**
- Controllers: `Controllers/`
- Services: `Services/`
- Repositories: `Repositories/`
- DTOs: `Dtos/`; AutoMapper profiles: `Mapping/`
- EF Core entities: `Models/`
- EF Core `DbContext`: `Properties/KnKDbContext.cs` (not under `Data/` or
  `Models/` as you might expect)
- Migrations: `Migrations/`
- `Data/` holds static JSON reference catalogs (Minecraft material/
  enchantment refs) — it is not the data-access layer
- Cross-cutting: `Middleware/`, `Attributes/`, `Configuration/`,
  `DependencyInjection/`, `Enums/`, `Json/`

**Conventions:**
- Auth: JWT bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`),
  configured under `Security:Jwt` in `appsettings.json` (issuer `knk-api`,
  audience `knk-app`). Access tokens carry `tv` (users.TokenVersion) and are
  checked on every request; refresh tokens are opaque, hashed in
  `refresh_tokens`, rotated on use and sent only as the HttpOnly `refreshToken`
  cookie on `/api/Auth`. `AuthService.RevokeAllSessionsAsync` ends every session
  of a user (password/email change, reset, deactivation, `POST api/Auth/logout-all`).
- **Default-deny** (`Attributes/DefaultCallerRequiredFilter.cs`, global): every
  MVC action needs the plugin key or a logged-in user unless it has
  `[AllowAnonymous]`, and every write (non-GET) also needs an explicit rule —
  `RequirePermission`, `RequireServiceOrPermission` (or the class-level
  `RequireServiceOrPermissionForWrites`), `RequirePluginService`,
  `RequireServiceSelfOrPermission`, `RequireServiceOrLoggedIn` or `Authorize` —
  or it answers 403 "This endpoint has no access rule.".
  `Tests/.../Security/EndpointAccessRulesTests.cs` lists offenders and pins the
  `[AllowAnonymous]` allow-list; add a rule to every new write endpoint.
- Staff nodes are in `StaffPermissions` (`Attributes/RequirePermissionAttribute.cs`).
  `knk.admin.content` gates content definitions (items, forms/displays, menus,
  reference data) and `knk.admin.world` the world model (domains, towns,
  districts, structures, streets, locations, gates, world tasks, workflows); both
  are matched by `knk.admin.*` and `*`. Granting nodes or groups goes through
  `PermissionEscalationGuard`: web staff can only hand out what they hold, never
  to themselves (unless they hold `*`).
- Rate limits (`Configuration/RateLimitingSetup.cs`): `[EnableRateLimiting("auth")]`
  / `("lookup")` on the auth and lookup endpoints, per client IP; behind a proxy
  set `ForwardedHeaders:Enabled` or every player shares one budget.
- No API versioning in place — routes follow the plain `api/[controller]`
  convention; no `Asp.Versioning`/`[ApiVersion]` usage found
- Observability: OpenTelemetry is wired in (see `OBSERVABILITY.md`),
  Prometheus metrics exposed at `/metrics`

**Talks to:** `knk-web-app` (browser clients with JWT bearer tokens) and
`knk-plugin` (REST through `knk-api-client`; protected service routes use
`X-API-Key` against `Security:PluginApiKey`). The plugin polls
`PlayerNotificationsController` for queued player notifications. Check
attributes per endpoint rather than assuming one authentication mode.
