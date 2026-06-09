---
name: spec-change
description: Update current product intent from a user-requested behavior change, preferring existing specs over new specs.
---

# spec-change

Use this skill when the user describes a desired product behavior change, new
capability, changed interaction rule, changed acceptance behavior, changed
default, or changed product constraint.

This skill updates `.specs/` before implementation.

Formula:

```text
spec-change = user change request + affected specs + minimal product intent update
```

## Rules

- Treat the user request as proposed product intent.
- First find whether the behavior belongs to an existing current spec.
- Prefer updating an existing current spec when the product area already exists.
- Create a new spec only when the change defines a distinct durable product
  area.
- Do not create a new spec for a local implementation task.
- Do not create a new spec for a small behavior change inside an existing
  feature area.
- Do not put implementation steps, temporary notes, generated plans, or chat
  history into specs.
- Do not archive old specs.
- If behavior changes inside the same product area, edit the existing spec.
- If product area identity changes, delete the old spec and create a new owning
  spec.
- If a document becomes obsolete, duplicated, task-like, process-only, or
  incorrect, delete it.
- Git history preserves previous versions.
- Update Behavior, Acceptance Criteria and Verification together when the change
  affects them.
- If the request contradicts current specs and the user clearly asks for the new
  behavior, update the spec to the new intent and mention the superseded
  behavior in the report.
- If the request is ambiguous, report the ambiguity and ask for the smallest
  product decision needed.
- Keep the change normative: describe observable product behavior, not patch
  mechanics.
- Do not treat current implementation as product intent by itself.

## Classification

Classify the request as one of:

```text
existing-spec-update
new-spec-required
adr-required
spike-required
task-only-no-spec-change
unclear-product-intent
```

Use `existing-spec-update` when an existing current spec already owns the product
area.

Use `new-spec-required` only when no existing current spec owns the product area
and the change describes durable product behavior.

Use `adr-required` when the change is primarily a durable architecture decision.

Use `spike-required` when the right product or architecture decision requires
research.

Use `task-only-no-spec-change` when the request is only a local refactor,
cleanup, dependency update, or implementation detail that does not change
durable product intent.

## Workflow

1. Read `.specs/README.md`.
2. Read `.specs/INDEX.md`.
3. Identify the product area and candidate current specs.
4. Read only relevant current numbered specs.
5. Classify the request.
6. If an existing spec owns the area, update that spec instead of creating a
   duplicate.
7. If a new spec is required, create it from the appropriate template and update
   `INDEX.md`.
8. If the change affects behavior, update acceptance criteria.
9. If the change affects testable behavior, update verification.
10. Report:

    - classification;
    - affected specs;
    - whether a new spec was created;
    - summary of semantic changes;
    - recommended implementation focus.

## Example

User request:

```text
When command-line completion is visible, Enter should not automatically accept
the first history suggestion. The default selected item should mean "no
completion"; Enter should execute the typed command unchanged. A real suggestion
is accepted only after the user explicitly selects it with keyboard or mouse.
```

Expected behavior:

- classify as `existing-spec-update`;
- read `.specs/0018.spec-command-history-completion.md`;
- update visible-panel command completion behavior;
- update acceptance criteria and manual verification;
- do not create a new spec.
