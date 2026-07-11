---
name: idd-intent-new-document
description: Create a new owning IDD spec, ADR, or spike only when no existing current document owns the area or decision.
---

# idd-intent-new-document

Use this skill to create a new owning specification, ADR, or spike when no
existing current document owns the product area or decision.

## Input

The request may explicitly specify the document type:

```text
type: spec | adr | spike
```

Use the requested type when it matches the change. If the type is not
specified, infer it from the change. If the requested type conflicts with IDD
rules, state the mismatch and use the correct document type.

This skill accepts a semantic handoff from `idd-intent-change`,
`idd-intent-brainstorm`, or Factory. The handoff includes document type,
product or decision area, durable intent, why current documents are not valid
owners, related documents, acceptance or decision context, and open questions.

## Rules

- Do not use this skill for changing behavior already covered by an existing
  current spec.
- Use `idd-intent-change` for user-requested changes to existing product behavior.
- Use `idd-intent-new-document` only when a new durable product area, ADR, or spike is
  needed.
- Do not create a spec for task-level changes.
- Do not create a spec for an ordinary dependency update.
- Create a spec only for durable product intent.
- Create an ADR for durable architectural decisions.
- Create a spike for research before a decision.
- Do not create replacement specs only to preserve old wording.
- If the product area is the same, update the existing spec.
- If the product area identity changes, create the new owning spec only after
  this skill's ownership check confirms that no current document owns it.
- Git history preserves the deleted document.
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

1. Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and relevant current numbered
   documents directly under `.idd/intent/`.
2. Determine the document type from the explicit input or from the change.
3. Before creating a new document, search `INDEX.md` and relevant current specs
   for an existing owner of the product area.
4. If an owner exists, stop and use `idd-intent-change`.
5. If current intent already exists, update the existing current document
   instead of creating a duplicate.
6. Find the next number by scanning current numbered documents directly under
   `.idd/intent/`. Do not scan or create an archive directory. Deleted document
   numbers are not reused.
7. Create the document from the matching template.
8. Update `INDEX.md` when a numbered document is added.
9. Keep the document normative. Do not add local task notes.
