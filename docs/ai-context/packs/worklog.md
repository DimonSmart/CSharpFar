---
id: worklog-rules
scope: [codex, copilot]
category: process
requires: [ai-rules]
---
# Worklog

This project uses worklog-driven development.

Before significant project work:
- read `docs/worklog-method.md`
- inspect relevant `.worklog/*.active.md` files
- decide whether the change needs a new work document
- if yes, create the next `NNNN.type-short-title.active.md` file from `.worklog/_templates/` using `spec`, `adr`, or `spike`

Do not create a work document for micro-changes, small product-neutral changes, or fixes: typos, formatting, obvious local fixes, small refactoring with no behavior or architecture meaning, dependency patches with no project-specific decision, or anything fully explained by a commit message.

If a change does not introduce architecture decisions, library choices, experiments, new capabilities, changed requirements, or user-visible behavior changes, keep it out of `.worklog/` and describe it in the commit message. When unsure about a small change, do not create a work document by default.

Use only `.active.md` files as current context. `.retired.md` files are history; read them only when an active document references them through `Replaces`, the user asks for history, or the task is to explain a decision.

When an active document needs substantial semantic changes, rename the old file to `.retired.md`, create one or more new `.active.md` documents with new numbers, and add `Replaces:` with the old document number in each new document. Do not rewrite the semantic content of retired documents.

When completing work tracked by a work document, update its `Outcome` section with implementation summary, verification, deviations, and follow-up.

Accepted ADRs and old requirements are immutable. If a decision or requirement changes, create a new active document and retire the old one.
