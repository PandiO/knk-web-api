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
  `knkwebapi_v2.csproj`; the test project under `Tests/` is excluded from
  the main build via `DefaultItemExcludes`, which lists both `tests\**`
  and `Tests\**` so the exclude also works on case-sensitive Linux)
- Build: `dotnet build`
- Test: `dotnet test Tests/knkwebapi_v2.Tests/knkwebapi_v2.Tests.csproj`
  (capital `T` — the path is case-sensitive on Linux)
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
  audience `knk-app`). JWTs are for web-app users only. Staff-only web
  endpoints use `[RequirePermission(node)]` (`Attributes/`), which needs a
  JWT, so it must not go on routes the plugin calls. See **Talks to**
  below for how the plugin authenticates.
- No API versioning in place — routes follow the plain `api/[controller]`
  convention; no `Asp.Versioning`/`[ApiVersion]` usage found
- Observability: OpenTelemetry is wired in (see `OBSERVABILITY.md`):
  ASP.NET Core metrics, exported over OTLP when enabled. There is no
  Prometheus `/metrics` endpoint yet; it's a TODO in `Program.cs` and
  `OBSERVABILITY.md`.

**Talks to:** `knk-web-app` (browser clients, bearer token in the
`Authorization` header) and `knk-plugin` (direct REST calls from its
`knk-api-client` module). The plugin does **not** send a JWT:
- On `master`, the plugin calls anonymously (its `config.yml` ships
  `api.auth.type: none`) and most plugin-called routes are open. It can
  send a shared key in `X-API-Key` (`api.auth.type: apikey`), matched
  against `Security:PluginApiKey`. When that setting is set, the API uses
  the key in two places. `UsersController` only trusts the
  `X-Acting-User-Id` header (the in-game staff member named for audit
  logs) on requests carrying the key. `[RequirePluginServiceKey]` (siege
  match writes, one gate-structure endpoint) requires it, using
  `Security:PluginServiceKey` when set, else `Security:PluginApiKey`.
  Both checks are opt-in: with the settings empty (the default),
  everything stays open.
- KNG-22 (unmerged branch `claude/currency-payments`) makes the key
  mandatory and fails closed. It adds `[RequireServiceOrPermission(node)]`
  (the plugin's key, or a web user's JWT holding `node`) and
  `[RequirePluginService]` (plugin key only) in
  `Attributes/RequireServiceOrPermissionAttribute.cs`. There's a
  Development-only escape hatch, `Security:AllowUnauthenticatedPluginCalls`.

There is no webhook/push from the API to the plugin. The plugin polls
instead: e.g. `GET api/PlayerNotifications/pending`, and pending headless
WorldTasks.
