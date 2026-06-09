# Claude Instructions

This project uses Intent-Driven Development.

Current product intent lives in `.specs/`.

Use IDD only when working with durable product intent.

Do not load the whole `.specs/` directory by default. Read
`.specs/README.md`, `.specs/INDEX.md`, then only relevant numbered specs.

Use IDD skills for specific workflows:
- `spec-audit`
- `spec-change`
- `spec-create`
- `spec-implement`
- `spec-import`
- `spec-lint`
- `spec-reorganize`
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
the feature extends an existing product area. Use `spec-create`
only if the feature needs a new durable product area, ADR, or
spike.

Do not create a new spec merely because the user described a new
task. Prefer updating the existing owning spec.

Do not put local tasks, temporary implementation notes, generated plans, or chat
history into `.specs/`.

This file and installed IDD skills are workflow guidance.
They are not product specifications.
