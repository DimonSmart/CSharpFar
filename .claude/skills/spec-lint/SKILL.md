---
name: spec-lint
description: Run mechanical `.specs/` consistency checks without editing files.
context: fork
agent: Explore
argument-hint: "[optional spec path or scope]"
allowed-tools: Read Glob Grep Bash
---

# spec-lint

Use this skill to perform cheap mechanical validation over `.specs`.

Formula:

```text
spec-lint = cheap mechanical validation, not semantic review
```

Use it when the user asks whether `.specs` is mechanically consistent.

## Rules

- Do not rewrite files.
- Do not reorganize specs.
- Do not perform broad semantic review.
- Do not resolve product conflicts.
- Report errors, warnings, and suggested fixes only.

## Checks

Check that:

- `.specs/README.md` exists;
- `.specs/INDEX.md` exists;
- every current spec listed in `INDEX.md` exists;
- every current numbered spec under `.specs/` is listed in `INDEX.md`;
- `.specs` has no archive directory;
- `.specs/import-report.md` does not exist;
- generated, import, task, progress, or process reports are not stored under
  `.specs`;
- `INDEX.md` has no `Archived` section;
- no current spec links to deleted document storage;
- no file under `.specs` references `.specs/archive/...`;
- skills do not contain an archive-enabling flag;
- skills do not contain an archive import action;
- skills do not recommend archiving obsolete specs;
- obsolete/task-like/process-only docs are reported as delete candidates, not
  preservation candidates;
- templates/support docs are not listed as current specs;
- required sections exist, or missing sections are reported;
- `Related`, `Replaces`, `Supersedes`, `Depends on`, and similar numeric
  relation references point to existing current numbered docs;
- Related Specifications links point to existing files or valid external
  references;
- specs do not contain obvious stale `.worklog` references except in
  source/history sections;
- specs do not contain task/progress/status language in normative sections;
- specs do not contain generated chat transcripts;
- specs do not contain obvious contradiction markers such as "supported" in
  Scope and "not implemented" in Non-goals for the same feature;
- ADR files use ADR-like structure;
- spike files are marked as non-normative research or unresolved
  investigation.

`spec-lint` must fail if:

- an archive directory exists under `.specs`;
- `.specs/import-report.md` exists;
- generated, import, task, progress, or process reports exist under `.specs`;
- `INDEX.md` contains an `Archived` section;
- `INDEX.md` links to deleted document storage;
- any file under `.specs` references `.specs/archive/...`;
- any numeric `Related`, `Replaces`, `Supersedes`, `Depends on`, or similar
  relation points to a missing current numbered doc;
- any skill contains an archive-enabling flag;
- any skill contains an archive import action;
- any skill recommends moving specs to archive;
- docs describe archive as a normal lifecycle.

Mechanical lint may flag suspicious wording. It must not claim to have completed
semantic review.

## Output Format

```md
# Spec Lint Report

## Result

pass | fail

## Errors

Problems that should be fixed.

## Warnings

Suspicious structure or wording.

## Suggested fixes

Concrete file-level recommendations.
```

## Examples

User request:

```text
Check whether `.specs` is mechanically consistent.
```

Expected behavior:

- use `spec-lint`;
- check `INDEX.md`, files, links, required sections, and stale `.worklog`
  references;
- report pass/fail and warnings;
- do not edit files.

## Non-goals

Do not use this skill to:

- rewrite specs;
- import source material;
- reorganize product areas;
- decide whether product behavior is correct;
- perform implementation conformance checks.

Use `spec-audit` for broad structural diagnostics.
