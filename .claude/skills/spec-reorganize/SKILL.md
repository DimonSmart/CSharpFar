---
name: spec-reorganize
description: Reorganize existing `.specs/` intent without changing product meaning.
---

# spec-reorganize

Use this skill to reorganize existing `.specs/` intent without changing product
meaning.

This skill requires a concrete reorganization focus.

Specification reorganization moves existing intent to a better location. It
changes where intent lives, not what the product means.

## Required Input

The request must provide at least one concrete focus:

1. Topic focus

   A topic to collect across current specifications.

   Examples:

   - mouse support in console controls
   - validation error behavior
   - background job idempotency
   - API authentication rules

2. Source focus

   A specific spec, section, or fragment to extract or move.

   Examples:

   - `0003.spec-console-ui.md`, section `Controls`
   - the mouse support paragraph in `0007.spec-table-view.md`
   - all session lifetime rules in authentication specs

3. Target focus

   An existing or desired target specification.

   Examples:

   - move this into `0012.spec-console-controls.md`
   - create a dedicated spec for console control behavior
   - consolidate these rules into the existing authentication spec

## Missing Focus Rule

Do not run this skill if the user only asks to:

- clean up specifications;
- improve specs;
- review specs;
- make specs better;
- reorganize everything;
- find problems generally;
- rewrite documentation.

If no concrete reorganization focus is provided, do not inspect or rewrite the
specification set.

Ask for a concrete focus instead:

```text
Please specify what intent should be reorganized: a topic to collect, a source
spec or section to extract, or a target spec to consolidate into.
```

## Rules

- Preserve product intent.
- Move existing intent only.
- Do not introduce new requirements silently.
- Do not delete requirements silently.
- Do not choose one side of a conflict.
- Do not treat implementation as product intent.
- Do not rewrite specs for style only.
- Do not normalize wording across the whole `.specs/` directory.
- Do not turn tasks, temporary status, generated output, or chat history into
  normative intent.
- Keep source-specific behavior in the source spec when it is not general.
- Replace moved duplicated text with references to the target spec.
- Update `INDEX.md` when documents are added, archived, renamed, or their roles
  change.
- Stop and ask for confirmation when the operation would change product meaning.

## Workflow

1. Identify the concrete reorganization focus from the request.
2. If no concrete focus is present, stop and ask for one.
3. Read `.specs/README.md`, `.specs/INDEX.md`, and relevant current numbered
   documents directly under `.specs/`.
4. Find current specification fragments related to the focus.
5. Classify found fragments as:

   - common behavior to move;
   - source-specific behavior to keep;
   - duplicate wording to replace with references;
   - possible conflicts;
   - unrelated mentions.

6. If conflicts are found, report them and do not resolve them silently.
7. Propose the target structure:

   - create a new spec; or
   - update an existing target spec; or
   - move a section from one spec to another.

8. Move only existing intent.
9. Replace moved fragments in source specs with short references.
10. Preserve local exceptions and source-specific behavior.
11. Update `INDEX.md` when the document set or document roles change.
12. Run relevant verification.

## Examples

Good request:

```text
Use spec-reorganize to collect all current intent about mouse support in console
controls and move it into a dedicated specification.
```

Good request:

```text
Use spec-reorganize to extract the Controls section from
0003.spec-console-ui.md into a dedicated console controls specification.
```

Bad request:

```text
Use spec-reorganize to clean up the specs.
```

Response:

```text
Cannot run spec-reorganize without a concrete reorganization focus.

Specify one of:
- a topic to collect;
- a source spec or section to extract;
- a target spec to consolidate into.
```

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

## Non-Goals

This skill does not:

- review specification quality in general;
- search for all possible problems;
- rewrite specs for style;
- update product intent;
- infer new requirements from implementation;
- create a new feature spec from a task;
- normalize the whole `.specs/` directory.
