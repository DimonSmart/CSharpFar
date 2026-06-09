---
name: spec-update-from-implementation
description: Update specs from confirmed implementation behavior intended as product intent.
---

# spec-update-from-implementation

Use this skill to update a current specification from verified implementation
behavior when the user explicitly confirms that the implementation represents
current product intent.

Implementation evidence is not product intent by itself.

Use `spec-update-from-implementation` when implementation behavior already exists
and the user explicitly confirms that this implementation is current product
intent.

Use `spec-change` when the user describes desired future behavior before
implementation.

## Rules

- Do not treat incidental implementation details as requirements.
- Do not copy code structure, private helper names, temporary workarounds, or
  framework defaults into product intent unless they define the product.
- Do not update a specification from implementation merely because the
  implementation exists.
- Require explicit user confirmation before making semantic changes.
- Preserve the distinction between observable behavior, domain contracts,
  architecture, verification rules, and local implementation mechanics.
- If implementation and specification differ but intent is unclear, report the
  difference and ask for confirmation instead of editing the specification.
- Update existing current specifications before creating new documents.
- Change only the smallest set of current documents needed to represent
  confirmed intent.
- Preserve stable document numbers and references.
- Do not rewrite accepted ADRs semantically. If the decision changed, create a
  replacing ADR.
- If confirmed implementation behavior contradicts multiple current
  specifications, update all affected specifications consistently or report the
  conflict.
- Update `INDEX.md` only when documents are added, archived, renamed, or their
  role changes.

## Workflow

1. Read `.specs/README.md`, `.specs/INDEX.md`, and relevant current numbered
   documents directly under `.specs/`.
2. Inspect the implementation and verification evidence.
3. Identify observable behavior and durable architecture that may represent
   current product intent.
4. Exclude incidental implementation details and temporary state.
5. Summarize the proposed semantic specification changes for user confirmation.
6. After confirmation, update the smallest set of current specification files.
7. Update `INDEX.md` only when document structure or document roles changed.
8. Run relevant verification.
