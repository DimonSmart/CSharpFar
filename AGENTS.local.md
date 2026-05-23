---
id: agents-local
scope: [codex, claude, gemini]
category: user-overrides
---
# Local AI agent overrides

This file holds project-specific rules and is **not managed by the template**.
It is safe to edit — template updates will not overwrite it.

Read `AGENTS.md` first, then apply the rules here with higher priority.

## Repo checks

<!-- Override or extend the defaults from docs/AI_RULES.md.
     Default: `dotnet build` then `dotnet test`
     Examples of project-specific additions:
       - dotnet build -c Release
       - dotnet test --configuration Release --no-build
       - dotnet format --verify-no-changes
-->

## Project-specific rules

<!-- Add durable corrections, preferences, and workflow rules below.
     Record rules when you give a correction you expect to stick permanently,
     or when a repeated mistake signals that a rule was missing. -->

- External console programs should use Far-like current-console execution: start
  without ShellExecute, without stdio redirection, without a new console window,
  wait for exit, then redraw the UI. Use shell associations for documents and
  other non-executable files.
- Before adding or changing UI controls, read `docs/controls-registry.md` and
  prefer extending an existing control when its responsibility already matches
  the requested behavior.
- Registered reusable controls must include mouse interaction by default; do not
  require separate per-use opt-in for basic focus, selection, toggling,
  activation, scrolling, or dropdown behavior.
- FarNet compatibility should be modeled as CSharpFar's native extension domain
  model, not as a proxy plugin layered on top of a separate CSharpFar plugin
  abstraction.
- Editor work should build CSharpFar's internal editor model first. FarNet
  editor compatibility should be exposed as boundary-facing API/facade methods
  over that model, not as a separate adapter or highlighter/plugin layer.
- Editor keyboard mappings should prefer original Far Manager editor mappings
  over generic Windows editor conventions when a conflict appears.
