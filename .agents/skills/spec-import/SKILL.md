---
name: spec-import
description: Import raw source material into a structurally normalized IDD `.specs/` structure.
---

# spec-import

Use this skill to import raw specification material into a normalized IDD
`.specs/` structure.

Formula:

```text
spec-import = source triage + structural normalization + normalized spec writing
```

Use it when old `.worklog` content, GitHub Spec Kit folders, issue/task docs,
ADRs, research notes, implementation notes, or other sources must become a
coherent current product specification set.

Import is a migration of meaning, not a mechanical conversion from old files to
new files. Source files are evidence. They are not the desired target structure.

## Default Modes

```yaml
mode: apply-safe
autoNormalize: true
conflictMode: report-only
allowNewSpecs: true
allowArchive: true
```

Supported modes:

```yaml
mode: propose | apply-safe
autoNormalize: true
conflictMode: report-only
allowNewSpecs: true
allowArchive: true
```

`apply-safe` may apply structural changes that preserve product meaning. It must
not resolve product conflicts or invent new product decisions.

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
specs unless a fragment defines durable product behavior.

## Structural Normalization

Do not preserve source file boundaries by default. Source files are evidence,
not the desired target structure. The target structure must follow durable
product intent areas.

Before writing files, build a normalized target structure and look for:

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

Typical product areas include:

- product overview;
- panels;
- command line;
- file operations;
- viewer;
- editor;
- shared text format / encoding / BOM / EOL;
- UI controls / dialogs;
- providers / virtual file systems;
- rendering / console viewport;
- settings;
- architecture decisions;
- spikes / unresolved research.

This is not a fixed enum. Prefer areas that match the actual product.

## Required Behavior

1. Read `.specs/README.md`, `.specs/INDEX.md`, and existing current specs when
   they exist.
2. Read the requested source files or directories.
3. Split source material into:
   - durable product intent;
   - architecture decision;
   - unresolved research / spike;
   - historical context;
   - task/progress/status notes;
   - implementation-only cleanup/refactor notes;
   - obsolete source-specific wrapper text.
4. Do not import task/progress/status material as current specs.
5. Do not preserve source file boundaries automatically.
6. Build the normalized target structure before writing.
7. Create a new spec only for a distinct durable product area.
8. Update an existing spec when imported intent belongs to an existing area.
9. Split mixed-scope source docs.
10. Merge multiple source docs when they describe one small area.
11. Extract repeated common models into shared specs.
12. Keep semantic conflicts visible and do not resolve them automatically.
13. Update `.specs/INDEX.md`.
14. Keep a short source reference only when it helps traceability; do not turn a
    spec into an imported journal.

## Source Triage

Identify the source methodology and conventions before importing. Look for
README or index files, templates, lifecycle markers, document types, archives,
generated files, task sections, ADRs, spikes, research, and implementation
sections.

Use source-specific conventions as hints only. Classify each document and
section by whether it expresses durable product intent.

For GitHub Spec Kit / Spec Driven Development-like sources:

- `spec.md` may contain durable product intent.
- `plan.md` usually contains implementation approach; import only
  product-level constraints.
- `tasks.md` is process by default and should not become current intent.
- `research.md` may become ADRs or spikes.
- `data-model.md` may contain durable domain contracts.
- `contracts/` may contain durable API or integration contracts.
- `quickstart.md` is usually guidance, not normative intent, unless it defines
  acceptance behavior.
- Checklists may contain acceptance or verification rules, but not task status.

## Import Inventory

Create an import inventory before writing target specs.

For each source, track:

```text
source path
detected type
detected lifecycle/status
main product area
import action
reason
target document
review notes
```

Possible import actions:

```text
import-current
import-archive
convert-to-adr
convert-to-spike
extract-fragments
skip-process-only
skip-generated
needs-review
```

## Fragment Classification

Classify sections and paragraphs, not only files.

Fragment categories:

```text
durable-current-intent
durable-obsolete-intent
architecture-rationale
uncertainty-or-research-question
acceptance-or-verification-rule
user-visible-behavior
domain-contract
product-defining-technical-constraint
implementation-note
temporary-status
task-step
backlog-item
chat-history
generated-output
test-output
file-list
source-wrapper
```

Import durable intent. Drop process noise.

## Conflict Handling

A conflict exists when two current or possibly-current fragments define
different behavior, constraints, APIs, defaults, compatibility rules, or
non-goals.

Example:

```text
Scope says feature X is supported.
Non-goals says feature X must not be implemented.
```

Do not choose one side silently. Instead:

- create or import only non-conflicting durable intent;
- report the conflict;
- add an explicit unresolved decision section when the target location is clear;
- recommend an ADR, spike, or product decision;
- avoid hiding the conflict inside rewritten prose.

If the conflict blocks a coherent normative spec, stop and ask for a product
decision.

## Normalized Writing Rules

Create target documents by durable product area, not by source file.

Prefer:

- one shared spec for common reusable behavior;
- feature specs for user-visible capabilities;
- ADRs for durable architectural decisions;
- spikes for unresolved questions;
- archive files for useful obsolete intent.

Avoid:

- one imported spec per old task;
- one imported spec per old implementation step;
- duplicate specs for the same behavior;
- specs named after temporary work items;
- specs that describe how the migration was performed.

## Workflow

1. Read `.specs/README.md`, `.specs/INDEX.md`, and relevant existing current
   specs.
2. Read the requested source roots.
3. Discover source methodology and lifecycle conventions.
4. Build the import inventory.
5. Classify fragments into product intent, ADR, spike, historical context,
   process noise, cleanup/refactor notes, wrappers, and conflicts.
6. Build a product area map.
7. Perform structural normalization:
   - split oversized or mixed-scope material;
   - merge tiny related fragments into existing areas;
   - extract shared models;
   - separate ADR and spike material;
   - reject task/refactor/cleanup notes as current specs.
8. Propose or infer target files.
9. Write normalized current specs, ADRs, spikes, or archive material according
   to mode and safety.
10. Keep conflicts visible and unresolved.
11. Update `.specs/INDEX.md`.
12. Write an import report for non-trivial imports.
13. Run relevant repository checks.

## Import Report

For non-trivial imports, create an import report outside current numbered specs,
for example `.specs/import-report.md` or
`.specs/archive/import-report-YYYYMMDD.md`.

Include:

- source roots inspected;
- source methodology detected;
- source files skipped and why;
- source files imported and target documents;
- fragments extracted from task/process documents;
- structural normalization decisions;
- conflicts found;
- obsolete documents archived;
- documents requiring human review;
- shared topics consolidated;
- source-to-target mapping.

The report is not normative product intent.

## Quality Gate

Before finishing, check:

- No task steps were imported as product requirements.
- No progress/status notes were imported as normative intent.
- No implementation-only cleanup/refactor notes became current specs.
- No file lists, generated output, test output, or chat transcripts were
  imported.
- Durable behavior from task-like documents was not lost.
- Source boundaries were not preserved by default.
- Mixed-scope sources were split.
- Small related sources were consolidated.
- Cross-cutting topics were extracted to shared specs.
- Existing specs were updated when appropriate.
- Conflicts are visible and unresolved.
- ADR-worthy decisions and spike-worthy research are separated.
- `.specs/INDEX.md` is updated.
- The resulting specs describe target product state, not work history.

## Examples

### Import mixed old material

Input:

```text
Old `.worklog` contains:
- one large MVP document;
- separate notes about viewer/editor encoding;
- cleanup task about removing unused fields;
- conflicting copy behavior about Append.
```

Expected import behavior:

- split the MVP document into product overview and area specs;
- extract shared text encoding/BOM/EOL behavior into a dedicated spec;
- do not import the cleanup task as a current product spec;
- report the Append conflict as a product decision;
- update `.specs/INDEX.md`.

### Spec Kit-like source

Input:

```text
feature-x/
- spec.md
- plan.md
- tasks.md
- research.md
- contracts/api.yaml
```

Expected import behavior:

- import durable behavior from `spec.md`;
- import durable API contracts from `contracts/api.yaml`;
- convert durable architecture decisions from `research.md` into ADR material;
- convert unresolved research into a spike;
- skip `tasks.md` as process;
- use `plan.md` only for product-defining constraints.

## Non-goals

Do not use this skill for:

- full quality review of all existing specs when import was not requested;
- broad diagnostics of current `.specs` structure without import;
- rewriting specifications just to make them nicer;
- deriving requirements from code;
- automatically resolving product conflicts;
- moving tasks into `.specs/`;
- creating a project plan or implementation backlog.

Use `spec-audit` for broad structural diagnostics without edits. Use
`spec-reorganize` for focused normalization of existing current specs after
import.
