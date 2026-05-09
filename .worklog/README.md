# Worklog

This directory contains the project worklog.

Small product-neutral changes and fixes are described in commit messages, not here.

Use one increasing sequence for all significant work documents:

`NNNN.type-short-title.lifecycle.md`

Lifecycle suffixes:

- `.active.md` - current source of requirements, decisions, or constraints.
- `.retired.md` - historical document. Do not use it as current requirements.

Draft documents are not committed to the main branch.

Types:

- `spec` - feature or behavior change;
- `adr` - architecture decision;
- `spike` - investigation.

There is no `task` worklog type. Small local changes belong in commit messages unless they require a `spec`, `adr`, or `spike`.

Use only `.active.md` files as default work context. Read `.retired.md` files only for explicit history questions, decision explanation, or when an active document references them through `Replaces`.

When an active document needs substantial semantic changes:

1. Rename the old file from `.active.md` to `.retired.md`.
2. Create one or more new `.active.md` documents with new numbers.
3. In each new document, add `Replaces:` with the old document number.
4. Do not rewrite the semantic content of the retired document.

References use document numbers only, not filenames.

Do not split documents into separate folders by type.

Use templates from `_templates`.

Before creating a new document, check the latest number.

Before implementation, make sure the document has:

- clear goal;
- scope;
- non-goals if applicable;
- done criteria;
- verification plan.

After implementation, update `Outcome`.

Do not add a numbered document for micro-changes, typos, formatting, obvious local fixes, small refactoring with no behavior or architecture meaning, dependency patches with no project-specific decision, or changes that do not affect the product as such. If a small change is fully explained by the diff and commit message, keep it out of `.worklog`.
