# CLAUDE.md — knk-web-api

## Global project context
@../../docs/ai-agents/GLOBAL_AGENT_INSTRUCTIONS.md

If the import above didn't load (e.g. this repo isn't checked out inside a
`knk-workspace` checkout at `Repository/knk-web-api` on this machine — that's
the current layout, but it may differ on other machines), here are the
essentials it contains:

- Knights and Kings V3 = `knk-web-app` (React/TS) + `knk-web-api`
  (ASP.NET Core) + `knk-plugin` (Spigot/Paper), one shared MySQL DB. Most
  features span all three repos.
- Current priority: reach MVP, siege minigame is the headline feature.
- The developer works on this evenings/weekends around a full-time job —
  don't require synchronous mid-week decisions; leave sessions in a clean,
  resumable state with clear handoff notes.
- Multiple sessions often run in parallel across repos on the same feature.
  Check and update `knk-workspace/docs/ACTIVE_SESSIONS.md` before and after
  working, and scope your claim by feature, not just by repo.
- Docs live in `knk-workspace` under `vision/ architecture/ guides/
  ai-agents/ specs/ backlog/ reports/ archive/` — don't scatter new docs
  elsewhere.

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
  `knkwebapi_v2.csproj`; the test project under `tests/` is excluded from
  the main build via `DefaultItemExcludes`)
- Build: `dotnet build`
- Test: `dotnet test tests/knkwebapi_v2.Tests/knkwebapi_v2.Tests.csproj`
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

**Talks to:** `knk-web-app` (browser clients, bearer token in the
`Authorization` header) and `knk-plugin` (direct REST calls from its
`knk-api-client` module, also bearer-token authenticated via
`BearerAuthProvider`) — no webhook/push model from the API side was found.
