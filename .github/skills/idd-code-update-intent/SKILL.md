---
name: idd-code-update-intent
description: Update `.idd/intent/` product intent from confirmed implementation behavior.
---

# idd-code-update-intent

Use this skill to update a current specification from verified implementation
behavior when the user explicitly confirms that the implementation represents
current product intent.

Implementation evidence is not product intent by itself.

Use `idd-code-update-intent` when implementation behavior already exists
and the user explicitly confirms that this implementation is current product
intent.

Use `idd-intent-change` when the user describes desired future behavior before
implementation.

## Rules

- Do not treat incidental implementation details as requirements.
- Do not copy code structure, private helper names, test method names, source
  file names, internal naming, constructor signatures, current dependency
  injection graph, temporary fallbacks, migration state, command lines, or
  framework defaults into product intent unless they define a public or durable
  architecture contract.
- Do not copy a package version unless that version is itself a compatibility
  contract.
- Before transferring behavior from implementation, express it without internal
  type names. If that is impossible, determine whether the type is a public or
  durable architecture contract before updating intent.
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
- Do not archive old specs when implementation changes confirmed product
  intent.
- Update current owning specs directly.
- If confirmed implementation replaces a whole product area, delete the old
  spec and create a new owning spec.
- Git history preserves previous versions.
- If confirmed implementation behavior contradicts multiple current
  specifications, update all affected specifications consistently or report the
  conflict.
- Update `INDEX.md` only when documents are added, deleted, renamed, or their
  role changes.

## Workflow

1. Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and relevant current numbered
   documents directly under `.idd/intent/`.
2. Inspect the implementation and verification evidence.
3. Identify observable behavior and durable architecture that may represent
   current product intent.
4. Express candidate behavior without internal type names, then exclude
   incidental implementation details and temporary state.
5. Summarize the proposed semantic specification changes for user confirmation.
6. After confirmation, update the smallest set of current specification files.
7. Update `INDEX.md` only when document structure or document roles changed.
8. Run relevant verification.
