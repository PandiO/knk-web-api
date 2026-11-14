# Knights and Kings web API (knk-web-api)

The backend of Knights and Kings, a Minecraft MMO: an ASP.NET Core API on .NET 8 with EF Core on MySQL (Pomelo). The
web app (`knk-web-app`) calls it with JWT bearer tokens, the Paper plugin (`knk-plugin`) with the plugin key
(`X-API-Key`); both also use its SignalR hubs.

## Quick start

You need the .NET 8 SDK and a MySQL server. `appsettings*.json` hold no secrets: set them once per machine with
user-secrets from the repo root (the full list, including the plugin key and SMTP password, is in [CLAUDE.md](CLAUDE.md)):

```bash
dotnet user-secrets set "ConnectionStrings:MySqlDbConnection" "Server=localhost;Database=knightsandkings_dev_v2;User=<user>;Password=<password>;Allow User Variables=True"
dotnet user-secrets set "Security:Jwt:Secret" "<output of: openssl rand -base64 48>"

dotnet tool install --global dotnet-ef --version 9.0.10   # once; matches Microsoft.EntityFrameworkCore.Design
dotnet restore
dotnet ef database update     # apply the migrations
dotnet run                    # http://localhost:5294, Swagger at /swagger in Development
dotnet test Tests/knkwebapi_v2.Tests/knkwebapi_v2.Tests.csproj
```

CI runs on every push and pull request: `build-and-test` (`.github/workflows/ci.yml`) and `fresh-database`
(`.github/workflows/migrations.yml`, all migrations against an empty MySQL).

## Documentation

All documentation lives in [knk-workspace](https://github.com/PandiO/knk-workspace/tree/main/docs):

1. [Web API architecture](https://github.com/PandiO/knk-workspace/blob/main/docs/architecture/web-api-architecture.md)
   and the [web API reading guide](https://github.com/PandiO/knk-workspace/blob/main/docs/guides/web-api-reading-guide.md).
2. [Observability](https://github.com/PandiO/knk-workspace/blob/main/docs/architecture/web-api-observability.md):
   health endpoints, client registry, OpenTelemetry and the Prometheus endpoint.
3. [Production installation](https://github.com/PandiO/knk-workspace/blob/main/docs/guides/production-installation.md)
   for every setting and secret on a server.

Agents and contributors: start at [AGENTS.md](AGENTS.md) and [CLAUDE.md](CLAUDE.md).
