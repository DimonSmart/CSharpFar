# Agent Instructions

This project uses Intent-Driven Development.

Current product intent lives in `.idd/intent/`.

Use IDD only when working with durable product intent.

Do not load the whole `.idd/intent/` directory by default. Read
`.idd/intent/README.md`, `.idd/intent/INDEX.md`, then only relevant numbered specs.

Use installed IDD skills for specific workflows:
- `idd-code-check-implementation`
- `idd-code-implement`
- `idd-code-update-intent`
- `idd-intent-audit`
- `idd-intent-brainstorm`
- `idd-intent-change`
- `idd-intent-import`
- `idd-intent-lint`
- `idd-intent-new-document`
- `idd-intent-normalize-current`

## IDD Workflow Routing

Use `idd-intent-brainstorm` when product intent is unclear.
Use `idd-skip` only when the user explicitly invokes it for the current
request. Never select `idd-skip` automatically.
Use `idd-intent-change` when durable product behavior must change.
Use `idd-code-implement` for one focused behavior already covered by
`.idd/intent/`, then use `idd-code-check-implementation`.
Use `idd-intent-new-document` only for a new durable product area, ADR, or
spike.

Do not create a new spec merely because the user described a new task. Prefer
updating the existing owning spec.

Do not put local tasks, temporary implementation notes, generated plans, or chat
history into `.idd/intent/`.

## Document Lifecycle

Git stores history.

`.idd/intent/` stores only current product intent, ADRs, and active spikes.

There is no `.idd/intent` archive lifecycle.

Do not move obsolete specs to an archive. Delete obsolete, duplicated,
task-like, process-only, or incorrect documents from the working tree.

When product intent evolves inside the same product area, update the existing
spec directly.

When a product area is replaced by a substantially different product area,
delete the old spec and create a new owning spec.

ADRs are decision records. Do not archive superseded ADRs. Mark them as
`Superseded` and create a new ADR for the replacing decision.

Resolved spikes should be deleted after their outcome is captured in a spec or
ADR, unless they remain useful as active research.

This file and installed IDD skills are workflow guidance.
They are not product specifications.
