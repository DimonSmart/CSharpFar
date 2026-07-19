# Gemini Instructions

This project uses Intent-Driven Development.

Current product intent lives in `.idd/intent/`.
Required IDD plugins are declared in `.idd/plugins.json`.

Use IDD only when working with durable product intent.

Do not load the whole `.idd/intent/` directory by default. Read
`.idd/intent/README.md`, `.idd/intent/INDEX.md`, then only relevant numbered specs.

This CodingAgent does not use generated IDD skills. Keep IDD work focused and
read only the documents needed for the current task. Skip IDD only when the
user explicitly requests it for the current request; never select skip
automatically.

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

This file is workflow guidance.
It is not a product specification.
