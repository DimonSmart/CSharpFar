---
name: idd-intent-audit
description: Diagnose `.idd/intent/` product intent structure and recommend reorganizations without editing files.
---

# idd-intent-audit

Use this skill to diagnose the structure of `.idd/intent` without editing files.

Formula:

```text
idd-intent-audit = broad structural diagnostics, no file edits
```

Use it for requests such as "review current `.idd/intent` structure", "find bad
split/merge decisions", "find structural problems", or "look across all specs".

## Rules

- Do not edit files.
- Do not reorganize specs.
- Do not resolve product conflicts.
- Do not read the whole project unless needed to understand spec references.
- Recommend `idd-intent-normalize-current` for focused follow-up work.
- Recommend `idd-intent-import` only when the problem is unnormalized raw source
  material.
- Report uncertainty explicitly.

## Current Spec Test

Current specs describe target product state, not the history of work.

A spec answers:

```text
If the implementation is deleted but the specs remain, can the product be rebuilt?
```

Therefore current specs may contain product behavior, user scenarios, domain
contracts, durable architecture patterns, durable technical constraints,
compatibility requirements, non-goals, acceptance criteria, and verification
rules.

Current specs must not contain local tasks, temporary implementation notes,
progress logs, chat history, one-off cleanup notes, plans that do not define
product behavior, source-specific wrapper text from imported methodologies, or
private code contracts and verification commands.

## Required Behavior

1. Read `.idd/intent/README.md`.
2. Read `.idd/intent/INDEX.md`.
3. Read headings, Intent, Scope/Behavior, Related specs, Non-goals, and
   Acceptance Criteria from current specs.
4. Do not read the whole project without necessity.
5. Build a product area map.
6. Look for:
   - oversized specs;
   - undersized specs;
   - mixed-scope specs;
   - duplicate specs;
   - scattered shared models;
   - stale imported artifacts;
   - task/refactor/cleanup specs;
   - semantic conflicts;
   - obsolete references;
   - `.idd/intent` archive directory;
   - `Archived` section in `INDEX.md`;
   - archive references in skills or docs;
   - obsolete documents that should be deleted;
   - process-only documents that should be deleted;
   - duplicated specs that should be merged or deleted;
   - ADRs incorrectly moved out of the current document set;
   - spikes that are resolved but still kept as current research;
   - specs that should be ADR;
   - specs that should be spike;
   - missing shared specs;
   - missing references between related specs.
   - implementation leakage: build/test command blocks, source files in
     normative sections, private-style identifiers, method-call syntax, test
     class or method names, constructor wiring, or dependency registration;
   - task-like headings, implementation sequences, migration history, and
     phrases such as `complete migration`, `finish migration`, `remaining work`,
     `update all usages`, or `remove legacy call sites`;
   - over-specified architecture that restricts a correct implementation without
     defining a durable product property.
7. Do not edit files.
8. Produce a report with recommendations.

## Structural Diagnostics

Use the same structural normalization concepts as `idd-intent-import` and
`idd-intent-normalize-current`, but only for diagnosis.

Look for durable product areas such as product overview, panels, command line,
file operations, viewer, editor, shared text format / encoding / BOM / EOL, UI
controls / dialogs, providers / virtual file systems, rendering / console
viewport, settings, architecture decisions, and spikes / unresolved research.
This is not a fixed enum.

## Report Format

```md
# IDD Intent Audit Report

## Summary

Short list of the most important structural problems.

## Product Area Map

| Area | Current specs | Notes |
|---|---|---|

## Findings

### Finding: <short title>

- Type: oversized | undersized | mixed-scope | duplicate | scattered-model | conflict | task-like-spec | stale-reference | missing-shared-spec | adr-candidate | spike-candidate | archive-concept | delete-candidate | obsolete-current-doc | resolved-spike | superseded-adr-status-missing | implementation-leakage | verification-command | migration-history | private-code-contract | over-specified-architecture
- Specs:
- Problem:
- Recommended action:
- Safety:
  - safe to generalize
  - likely durable architecture
  - requires human review
  - delete candidate

## Proposed Reorganization Plan

Ordered list of recommended split/merge/extract/delete actions.

## Product Decisions Required

Explicit list of conflicts or decisions that cannot be resolved mechanically.

## No-change Areas

Specs or areas that look coherent and should not be reorganized.
```

## Examples

User request:

```text
Review current `.idd/intent` structure and find bad split/merge decisions.
```

Expected behavior:

- use `idd-intent-audit`;
- do not edit files;
- produce findings and a reorganization plan;
- identify which follow-up actions should use `idd-intent-normalize-current`.

## Non-goals

Do not use this skill to:

- edit files;
- perform focused reorganization;
- import external source material;
- verify implementation against specs;
- lint mechanical consistency only.

Use `idd-intent-lint` for cheap mechanical validation.
