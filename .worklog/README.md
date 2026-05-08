# Worklog

This directory contains the project worklog.

Small product-neutral changes and fixes are described in commit messages, not here.

Use one increasing sequence for all significant work documents:

`NNNN.type.short-title.md`

Types:

- `spec` - feature or behavior change;
- `task` - small local change;
- `adr` - architecture decision;
- `spike` - investigation.

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

Do not add a numbered document for typos, formatting, obvious local fixes, small refactoring with no behavior or architecture meaning, dependency patches with no project-specific decision, or changes that do not affect the product as such.
