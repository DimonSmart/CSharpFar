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
- inspect the latest numbered files in `.worklog/`
- decide whether the change needs a new work document
- if yes, create the next `NNNN.type-short-title.md` file from `.worklog/_templates/`

Do not create a work document for small product-neutral changes or fixes: typos, formatting, obvious local fixes, small refactoring with no behavior or architecture meaning, dependency patches with no project-specific decision, or anything fully explained by a commit message.

If a change does not introduce architecture decisions, library choices, experiments, new capabilities, or user-visible behavior changes, keep it out of `.worklog/` and describe it in the commit message.

When completing work tracked by a work document, update its `Outcome` section with implementation summary, verification, deviations, and follow-up.

Accepted ADRs are immutable. If a decision changes, create a new ADR that supersedes the previous one.
