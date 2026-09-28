# Agent entrypoint — knk-web-api

Before editing, find `knk-workspace/docs/ai-agents/GLOBAL_AGENT_INSTRUCTIONS.md`
and `knk-workspace/docs/ACTIVE_SESSIONS.md` in the local workspace or
[online](https://github.com/PandiO/knk-workspace/tree/main/docs). They govern
cross-repo coordination for all agents. Refresh the tracker, check overlapping
claims, publish a feature-scoped claim, and use the feature's standing branch
from this repo's current default branch (`master` at this audit). If access
is unavailable, disclose it and avoid conflicting remote edits. Recheck old
handoffs against current code and plans. See [CLAUDE.md](CLAUDE.md) for
repo-specific commands; its `@...` import is Claude Code syntax and may
depend on checkout layout.

This is the ASP.NET Core API with EF Core/MySQL. Check the actual
`TargetFramework` in `knkwebapi_v2.csproj` before choosing SDK commands.
Build with `dotnet build`; test with
`dotnet test Tests/knkwebapi_v2.Tests/knkwebapi_v2.Tests.csproj`.
