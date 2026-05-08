---
id: ai-rules
scope: [codex, copilot]
category: rules
requires: [core-rules, dotnet-rules]
---
# AI Rules

Start here for repository-wide AI guidance.

Core:
- Read `docs/ai-context/core.md` for design principles and general coding rules.
- Read `docs/ai-context/dotnet.md` when changing C# or .NET code.
- Apply every file in `docs/ai-context/packs/` when present.

Skills:
- Load detailed skill references from `docs/ai-skills/` when a matching local skill points to them.

References:
- `docs/ai-context/references/code-review-checklist.md`
- `docs/ai-context/references/testing-checklist.md`
- `docs/ai-context/references/public-api-checklist.md`

Working rules:
- Prefer minimal diffs and existing project patterns.
- Do not add compatibility layers unless compatibility is requested.
- Ask before breaking public behavior or public APIs.
- Run repo checks before finishing.

## Repo checks

Default commands. Override in `AGENTS.local.md` when the project requires different commands:
- `dotnet build`
- `dotnet test`
