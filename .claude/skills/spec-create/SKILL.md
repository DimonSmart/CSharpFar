---
name: spec-create
description: Create a new IDD spec, ADR, or spike for durable product intent.
---

# spec-create

Use this skill to create a new specification, ADR, or spike.

## Input

The request may explicitly specify the document type:

```text
type: spec | adr | spike
```

Use the requested type when it matches the change. If the type is not
specified, infer it from the change. If the requested type conflicts with IDD
rules, state the mismatch and use the correct document type.

## Rules

- Do not use this skill for changing behavior already covered by an existing
  current spec.
- Use `spec-change` for user-requested changes to existing product behavior.
- Use `spec-create` only when a new durable product area, ADR, or spike is
  needed.
- Do not create a spec for task-level changes.
- Do not create a spec for an ordinary dependency update.
- Create a spec only for durable product intent.
- Create an ADR for durable architectural decisions.
- Create a spike for research before a decision.
- If the requested type does not match the change, do not follow it blindly.
  State the mismatch and use the correct IDD document type.

## Document Type

- `spec` - durable product behavior, domain contracts, acceptance criteria,
  verification rules, shared behavior.
- `adr` - durable architectural decision where rationale, alternatives, and
  tradeoffs matter.
- `spike` - research, experiment, or hypothesis check before committing to
  product or architecture intent.

## Workflow

1. Read `.specs/README.md`, `.specs/INDEX.md`, and relevant current numbered
   documents directly under `.specs/`.
2. Determine the document type from the explicit input or from the change.
3. Before creating a new document, search `INDEX.md` and relevant current specs
   for an existing owner of the product area.
4. If an owner exists, stop and use `spec-change`.
5. If current intent already exists, update the existing current document
   instead of creating a duplicate.
6. Find the next number by scanning `.specs/` and `.specs/archive/`.
7. Create the document from the matching template.
8. Update `INDEX.md` when a numbered document is added.
9. Keep the document normative. Do not add local task notes.
