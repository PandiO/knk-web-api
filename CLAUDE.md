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
  audience `knk-app`)
- No API versioning in place — routes follow the plain `api/[controller]`
  convention; no `Asp.Versioning`/`[ApiVersion]` usage found
- Observability: OpenTelemetry is wired in (see `OBSERVABILITY.md`),
  Prometheus metrics exposed at `/metrics`

**Talks to:** `knk-web-app` (browser clients with JWT bearer tokens) and
`knk-plugin` (REST through `knk-api-client`; protected service routes use
`X-API-Key` against `Security:PluginApiKey`). The plugin polls
`PlayerNotificationsController` for queued player notifications. Check
attributes per endpoint rather than assuming one authentication mode.
