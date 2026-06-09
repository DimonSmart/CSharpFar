# Agent Instructions

This project uses Intent-Driven Development.

Current product intent lives in `.specs/`.

Use IDD only when working with durable product intent.

Do not load the whole `.specs/` directory by default. Read
`.specs/README.md`, `.specs/INDEX.md`, then only relevant numbered specs.

Use IDD skills for specific workflows:
- `spec-audit`
- `spec-change`
- `spec-implement`
- `spec-import`
- `spec-lint`
- `spec-new-document`
- `spec-normalize-current`
- `spec-check-implementation`
- `spec-update-from-implementation`

## IDD Workflow Routing

When the user asks to change product behavior: use `spec-change`,
then `spec-implement`, then `spec-check-implementation`.

When the user asks to implement behavior already described in
`.specs/`: use `spec-implement`, then `spec-check-implementation`.

When the user reports a possible bug: use
`spec-check-implementation`; if the current spec is clear, fix
implementation with `spec-implement`; if the desired behavior
changes product intent, use `spec-change` first.

When the user asks to create a new feature: use `spec-change` if
the feature extends an existing product area. Use `spec-new-document`
only if the feature needs a new durable product area, ADR, or
spike.

Do not create a new spec merely because the user described a new
task. Prefer updating the existing owning spec.

Do not put local tasks, temporary implementation notes, generated plans, or chat
history into `.specs/`.

## Document Lifecycle

Git stores history.

`.specs/` stores only current product intent, ADRs, and active spikes.

There is no `.specs` archive lifecycle.

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
