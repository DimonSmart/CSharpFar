---
name: idd-intent-normalize-current
description: Perform focused structural normalization of existing `.idd/intent/` product intent without changing product meaning.
argument-hint: --documents <ids> --topic <topic> --target <file|new> --mode propose|apply
---

# idd-intent-normalize-current

Use this skill to perform focused structural normalization over accepted current
specs without changing product meaning.

Formula:

```text
idd-intent-normalize-current = focused structural normalization over accepted current specs
```

This skill works on an already accepted `.idd/intent` structure, so it is more
cautious than import. It requires a concrete focus and must not run a broad
rewrite of `.idd/intent`.

Use it for later maintenance of an existing `.idd/intent` tree. Do not use it as a
manual cleanup phase required after `idd-intent-import`; a successful import already
includes the normalization needed for mechanical consistency.

## Parameters

Describe the operation with parameters like:

```yaml
scope:
  specs: [0019, 0054]
  topic: "Paranoid copy retry"
  target: "0019.spec-paranoid-copy-resume.md"

mode: propose | apply
allowNewSpec: true | false
preserveMeaning: true
```

`preserveMeaning` is mandatory. If the operation would change product meaning,
stop and report the required product decision.

## Required Input

The request must provide at least one concrete focus:

1. Topic focus: a topic to collect across current specifications.
2. Source focus: a specific spec, section, or fragment to extract or move.
3. Target focus: an existing or desired target specification.
4. Mechanical cleanup focus: an existing lint failure such as stale archive
   directory, process report under `.idd/intent`, broken obvious relation remap,
   stale index, or legacy section shape.

Examples:

```text
Use idd-intent-normalize-current with:
--specs 0033,0046,0014,0048
--topic "text encoding / BOM / EOL"
--target new
--mode propose
```

```text
Use idd-intent-normalize-current with:
--specs 0019,0054
--topic "Paranoid copy retry"
--target 0019.spec-paranoid-copy-resume.md
--mode apply
```

## Missing Focus Rule

Do not run this skill if the user only asks to:

- clean up specifications;
- improve specs;
- review all specs;
- find structural problems;
- make specs better;
- reorganize everything;
- find problems generally;
- rewrite documentation.

If the request is broad, such as "review all specs" or "find structural
problems", do not run idd-intent-normalize-current. Use `idd-intent-audit` first.

If no concrete normalization focus is provided, do not inspect or rewrite the
specification set. Respond with:

```text
Cannot run idd-intent-normalize-current without a concrete normalization focus.

For broad structural diagnostics, use idd-intent-audit first. For normalization,
specify a topic to collect, a source spec or section to extract, or a target
spec to consolidate into.
```

## Current Spec Test

Current specs describe target product state, not the history of work.

A spec answers:

```text
If the implementation is deleted but the specs remain, can the product be rebuilt?
```

Therefore current specs may contain:

- product behavior;
- user scenarios;
- domain contracts;
- durable architecture patterns;
- durable technical constraints;
- compatibility requirements;
- non-goals;
- acceptance criteria;
- verification rules.

Current specs must not contain:

- local tasks;
- temporary implementation notes;
- progress logs;
- chat history;
- one-off cleanup notes;
- plans that do not define product behavior;
- source-specific wrapper text from imported methodologies.

Task, refactor, cleanup, progress, and status notes are not current product
specs unless they define durable product behavior. Delete or convert task-like
specs when they are not current product intent.

## Structural Normalization

Do not preserve existing file boundaries when the focused operation proves the
current boundaries are wrong. Existing specs are accepted current intent, but
their structure may still be oversized, undersized, mixed-scope, duplicated, or
misplaced.

For the requested focus, look for:

- oversized specs that must be split;
- tiny specs that should be merged into an existing area;
- mixed-scope specs that describe unrelated product areas;
- repeated common models that should become shared specs;
- semantic conflicts that require a product decision;
- task/refactor/cleanup notes that should not be current product specs;
- ADR-worthy architectural decisions;
- spike-worthy unresolved research;
- obsolete or source-specific wrapper text;
- duplicated behavior across current specs.

Typical product areas include product overview, panels, command line, file
operations, viewer, editor, shared text format / encoding / BOM / EOL, UI
controls / dialogs, providers / virtual file systems, rendering / console
viewport, settings, architecture decisions, and spikes / unresolved research.
This is not a fixed enum.

## Required Behavior

Support these scenarios:

- consolidate topic X from several specs into one existing spec;
- extract shared model X into a new shared spec;
- split one mixed-scope spec into several specs;
- merge tiny spec X into a larger existing spec;
- move misplaced behavior from one spec to another;
- delete task-like, process-only, obsolete, duplicated, or incorrect documents;
- delete `.idd/intent/archive`;
- remove import/process reports from `.idd/intent`;
- regenerate `INDEX.md` from actual current numbered documents;
- fix broken relation references where the remap is obvious;
- normalize current document shapes;
- replace moved fragments with references;
- update `INDEX.md`;
- preserve product meaning.

## Rules

- Preserve product intent.
- Move existing intent only.
- Do not introduce new requirements silently.
- Do not delete requirements silently.
- Do not choose one side of a conflict.
- Do not treat implementation as product intent.
- Do not rewrite specs for style only.
- Do not normalize wording across the whole `.idd/intent/` directory.
- Do not turn tasks, temporary status, generated output, or chat history into
  normative intent.
- Keep source-specific behavior in the source spec when it is not general.
- Replace moved duplicated text with references to the target spec.
- Update `INDEX.md` when documents are added, deleted, renamed, or their roles
  change.
- Do not archive obsolete documents.
- Delete documents that no longer belong in the current working tree.
- Git history is the only history mechanism.
- Remove process-only import reports from `.idd/intent`; if a persistent report is
  explicitly needed, move it outside `.idd/intent`.
- Numeric `Related`, `Replaces`, `Supersedes`, `Depends on`, and similar
  relation references must point to existing current numbered documents.
- Remove historical-only relations or rewrite them when the current target is
  obvious from the existing document set.
- Regenerate `INDEX.md` from actual current numbered documents when the index is
  stale.
- If deleting a document would lose current product intent, stop and report the
  conflict.
- Report unresolved semantic ambiguity instead of guessing.
- Stop and ask for confirmation when the operation would change product meaning.

## Workflow

1. Identify the concrete normalization focus.
2. If no concrete focus is present, stop and direct broad requests to
   `idd-intent-audit`.
3. Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and only relevant current
   numbered specs.
4. Find current fragments related to the focus.
5. Classify fragments as:
   - common behavior to move;
   - source-specific behavior to keep;
   - duplicate wording to replace with references;
   - misplaced behavior;
   - task-like/process material;
   - ADR-worthy decisions;
   - spike-worthy unresolved research;
   - possible conflicts;
   - unrelated mentions.
6. Build the focused normalized target structure.
7. In `propose` mode, report the proposed split/merge/extract/delete plan and
   stop before edits.
8. In `apply` mode, move only existing intent that preserves meaning.
9. Replace moved fragments in source specs with short references.
10. Preserve local exceptions and feature-specific behavior.
11. Keep conflicts visible and unresolved.
12. Update `INDEX.md` when the document set or document roles change.
13. For mechanical cleanup focus:
    - delete `.idd/intent/archive` if it exists;
    - remove process reports from `.idd/intent`;
    - regenerate `INDEX.md`;
    - fix broken relation references where the remap is obvious;
    - normalize document shapes;
    - report unresolved semantic ambiguity.
14. Run relevant verification.

## Conflict Handling

If two current specifications disagree, do not resolve the conflict as
reorganization.

Example:

```text
0004.spec-table-view.md says mouse wheel scrolls the table.
0009.spec-selection.md says mouse wheel changes current selection.
```

Response:

```text
This cannot be resolved as specification reorganization. It requires a product
intent decision.
```

## Examples

### Consolidate shared text encoding

User request:

```text
Consolidate text encoding / BOM / EOL across viewer, editor, quick view and
create-file dialog.
```

Expected behavior:

- read only relevant specs;
- propose or create a shared text-file-format-and-encoding spec;
- move common model there;
- leave feature-specific UI behavior in viewer/editor/quick-view/create-file
  specs;
- replace moved duplicate text with references;
- update `INDEX.md`.

### Extract from one spec

User request:

```text
Use idd-intent-normalize-current to extract the Controls section from
0003.spec-console-ui.md into a dedicated console controls specification.
```

Expected behavior:

- read `0003.spec-console-ui.md` and closely related specs;
- create or update the target controls spec if allowed;
- leave non-control console behavior in the source spec;
- replace the extracted section with a reference;
- update `INDEX.md`.

### Delete task-like spec

User request:

```text
This spec is no longer needed; it was task-like/refactor-only.
```

Expected behavior:

- confirm the document contains no current product intent;
- delete the document from the working tree;
- update `INDEX.md`;
- do not preserve a copy elsewhere.

### Broad request

Bad request:

```text
Use idd-intent-normalize-current to clean up the specs.
```

Expected response:

```text
Cannot run idd-intent-normalize-current without a concrete normalization focus.

Use idd-intent-audit first to find broad structural problems.
```

## Non-goals

This skill does not:

- review specification quality in general;
- search for all possible problems;
- rewrite specs for style;
- update product intent;
- infer new requirements from implementation;
- create a new feature spec from a task;
- normalize the whole `.idd/intent` directory.

Use `idd-intent-audit` for broad structural diagnostics. Use `idd-intent-import` when raw
external material is being imported.
