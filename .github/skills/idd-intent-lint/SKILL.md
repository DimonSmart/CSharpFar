---
name: idd-intent-lint
description: Run mechanical `.idd/intent/` consistency checks without editing files.
---

# idd-intent-lint

Use this skill to perform cheap mechanical validation over `.idd/intent`.

Formula:

```text
idd-intent-lint = cheap mechanical validation, not semantic review
```

Use it when the user asks whether `.idd/intent` is mechanically consistent.

## Rules

- Do not rewrite files.
- Do not reorganize specs.
- Do not perform broad semantic review.
- Do not resolve product conflicts.
- Report errors, warnings, and suggested fixes only.

## Checks

Check that:

- `.idd/intent/README.md` exists;
- `.idd/intent/INDEX.md` exists;
- every current spec listed in `INDEX.md` exists;
- every current numbered spec under `.idd/intent/` is listed in `INDEX.md`;
- `.idd/intent` has no archive directory;
- `.idd/intent/import-report.md` does not exist;
- generated, import, task, progress, or process reports are not stored under
  `.idd/intent`;
- `INDEX.md` has no `Archived` section;
- no current spec links to deleted document storage;
- no file under `.idd/intent` references `.idd/intent/archive/...`;
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
- normative spec sections do not contain fenced build/test shell commands,
  explicit task/progress sections, implementation checklists, migration status,
  or a spec lifecycle status.

`idd-intent-lint` must fail if:

- an archive directory exists under `.idd/intent`;
- `.idd/intent/import-report.md` exists;
- generated, import, task, progress, or process reports exist under `.idd/intent`;
- `INDEX.md` contains an `Archived` section;
- `INDEX.md` links to deleted document storage;
- any file under `.idd/intent` references `.idd/intent/archive/...`;
- any numeric `Related`, `Replaces`, `Supersedes`, `Depends on`, or similar
  relation points to a missing current numbered doc;
- any skill contains an archive-enabling flag;
- any skill contains an archive import action;
- any skill recommends moving specs to archive;
- docs describe archive as a normal lifecycle.
- an ordinary spec contains `Status: Current`, `Status: Superseded`,
  `Superseded by`, or another explicit lifecycle status;
- `INDEX.md` models ordinary specs as `Current`, `Completed`, `Superseded`, or
  another lifecycle status;
- a normative spec section contains a fenced shell block with a build or test
  command such as `dotnet build`, `dotnet test`, `npm test`, `mvn test`,
  `gradle test`, `cargo test`, or `pytest`;
- a normative spec contains an explicit task/progress section, implementation
  checklist, or migration status.

Warn, without failing automatically, when a normative spec contains source file
names such as `.cs`, `.java`, `.ts`, or `.py`; private-style identifiers;
method-call notation; constructor names; dependency-injection registration
instructions; test method names; CLI commands; or terms such as `remaining`,
`finish`, `complete migration`, or `follow-up implementation`. Public APIs and
durable architecture types may legitimately match these patterns.

Do not apply implementation-leakage checks to clearly non-normative sections
named `Source history`, `Migration source`, `Provenance`, `Imported from`, or
`Historical note`; those sections must not state current requirements.

Mechanical lint may flag suspicious wording. It must not claim to have completed
semantic review.

## Output Format

```md
# IDD Intent Lint Report

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
Check whether `.idd/intent` is mechanically consistent.
```

Expected behavior:

- use `idd-intent-lint`;
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

Use `idd-intent-audit` for broad structural diagnostics.
