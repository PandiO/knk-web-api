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

## Shipping a change

All documentation lives in `knk-workspace/docs/`; this repo keeps only its README, `AGENTS.md`,
`CLAUDE.md` and `.github/`. Follow `knk-workspace/docs/ai-agents/GLOBAL_AGENT_INSTRUCTIONS.md` § Shipping a change
([online](https://github.com/PandiO/knk-workspace/blob/main/docs/ai-agents/GLOBAL_AGENT_INSTRUCTIONS.md#shipping-a-change-documentation)):
fill in the PR template's Docs impact and Admin steps sections, and ship the essential docs (deploy notes, changelog
entry, status) before merge. At merge, close the feature issue, put the remaining docs in a `documentation`
sub-issue and start a new docs session for it right away with
[the docs-session prompt](https://github.com/PandiO/knk-workspace/blob/main/docs/ai-agents/docs-session-prompt.md).

Docs triggers in this repo: a migration means the seed (`knk-dev-db-seed`) and the migration steps in
`guides/production-installation.md`; a new or changed setting or secret means the configuration and secret tables
there (and the closed-alpha runbook when alpha needs it); a new or changed endpoint means `docs/specs/api/` and the
architecture docs.
