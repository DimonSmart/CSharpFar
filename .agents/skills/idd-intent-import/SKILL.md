---
name: idd-intent-import
description: Import raw source material into structurally normalized IDD intent under `.idd/intent/`.
---

# idd-intent-import

Use this skill to import raw intent material into a normalized IDD
`.idd/intent/` structure.

Formula:

```text
idd-intent-import = import + mandatory normalization + lint gate
```

Use it when old `.worklog` content, GitHub Spec Kit folders, issue/task docs,
ADRs, research notes, implementation notes, or other sources must become a
coherent current product intent document set.

Import is a migration of meaning, not a mechanical conversion from old files to
new files. Source files are evidence. They are not the desired target structure.

Import is not complete until the generated `.idd/intent` tree is mechanically
consistent.

For successful apply-safe import, the expected final state is:

- no `.idd/intent/archive`;
- no process-only import reports under `.idd/intent`;
- all current numbered documents are listed in `.idd/intent/INDEX.md`;
- all current documents listed in `.idd/intent/INDEX.md` exist;
- all numeric `Related`, `Replaces`, `Supersedes`, `Depends on`, and similar
  references point to existing current documents;
- imported current specs, ADRs, and active spikes follow the current document
  shape;
- `idd-intent-lint` would return no errors.

Warnings may remain only for genuinely semantic ambiguity. Mechanical errors
must be fixed before finishing the import.

## Default Modes

```yaml
mode: apply-safe
autoNormalize: true
conflictMode: report-only
allowNewSpecs: true
```

Supported modes:

```yaml
mode: propose | apply-safe
autoNormalize: true
conflictMode: report-only
allowNewSpecs: true
```

`apply-safe` may apply structural changes that preserve product meaning. It must
not resolve product conflicts or invent new product decisions.

## Current spec test

Current spec documents describe target product state, not the history of work.

A spec answers:

```text
If the implementation is deleted but the intent documents remain, can the product be rebuilt?
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

1. Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and existing current specs when
   they exist.
2. Read the requested source files or directories.
3. Split source material into:
   - durable product intent;
   - architecture decision;
   - unresolved research / spike;
   - obsolete source material;
   - task/progress/status notes;
   - implementation-only cleanup/refactor notes;
   - obsolete source-specific wrapper text.
4. Classify every implementation-adjacent fragment as one of:
   - durable-verification-property;
   - verification-command;
   - durable-architecture-boundary;
   - implementation-structure;
   - public-contract;
   - private-code-detail;
   - migration-step;
   - source-scan-instruction.
5. Do not import task/progress/status material as current specs.
5. Do not preserve source file boundaries automatically.
6. Build the normalized target structure before writing.
7. Create a new spec only for a distinct durable product area.
8. Update an existing spec when imported intent belongs to an existing area.
9. Split mixed-scope source docs.
10. Merge multiple source docs when they describe one small area.
11. Extract repeated common models into shared specs.
12. Keep semantic conflicts visible and do not resolve them automatically.
13. Build and apply a source-to-target remap before writing final relations.
14. Regenerate `.idd/intent/INDEX.md` from actual current numbered documents.
15. Run or simulate `idd-intent-lint` and fix mechanical errors before finishing.
16. Keep a short source reference only when it helps traceability; do not turn a
    spec into an imported journal.

Do not import shell commands, build/test command blocks, private class or method
names, file lists, implementation order, dependency-injection wiring steps,
temporary allowlists, migration steps, or source-scan instructions as intent.
Generalize an implementation-specific fragment only when its meaning can be
preserved without adding a new product decision. If generalization needs a
product decision, retain a finding for human review.

## Source Triage

Identify the source methodology and conventions before importing. Look for
README or index files, templates, lifecycle markers, document types, generated
files, task sections, ADRs, spikes, research, and implementation
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

Create an import inventory before writing target documents.

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
convert-to-adr
convert-to-spike
extract-fragments
skip-process-only
skip-generated
delete-obsolete
delete-duplicate
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
durable-verification-property
verification-command
durable-architecture-boundary
implementation-structure
public-contract
private-code-detail
migration-step
source-scan-instruction
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

Do not import obsolete or process-only source material into an archive. Skip or
delete source material that has no current product intent. Preserve old
versions only through Git history.

Never create `.idd/intent/archive`.
Never move obsolete imported documents into `.idd/intent/archive`.
Never preserve skipped, obsolete, duplicate, process-only, task-like, or
historical source files as archived specs.
Git history is the archive.

## Source-to-target Remap

Before writing final documents, build a source-to-target mapping for every
imported, skipped, merged, deleted, absorbed, or converted source document.

Track at least:

```text
source id/path
source title
source detected type
action
target document if any
absorbed-by document if any
reason
reference rewrite rule
```

Possible actions:

```text
import-current
convert-to-adr
convert-to-spike
extract-fragments
merge-into-existing
absorb-into-new
skip-process-only
skip-generated
delete-obsolete
delete-duplicate
needs-review
```

Rules:

- A numeric relation may be written only if the referenced target document
  exists in current `.idd/intent`.
- If source document A was absorbed by target document B, references to A must
  be rewritten to B when the relation is still meaningful.
- If source document A was skipped as process-only, duplicate, obsolete,
  generated, or historical-only, references to A must be removed or rewritten as
  source history, not kept as normative numeric references.
- If the correct remap cannot be inferred safely, do not leave a broken
  reference. Report the unresolved mapping as a blocking import issue.

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
- spikes for active unresolved questions.

Avoid:

- one imported spec per old task;
- one imported spec per old implementation step;
- duplicate specs for the same behavior;
- specs named after temporary work items;
- specs that describe how the migration was performed.

Create current spec documents only for durable current product intent. Create
adr documents only for durable decision records. Create spike documents only for
active unresolved research.

For example, import this source:

````md
Run:

```bash
dotnet build
dotnet test
```

UiCompositionHost must be created in Bootstrap and passed to every dialog
constructor.
````

as the durable outcome only:

```md
## Durable Architecture And Constraints

Application-owned UI surfaces and overlays share one composition lifecycle.

## Verification

Automated coverage verifies composition behavior for nested overlays and
viewport changes.
```

Do not import the commands or constructor wiring.

Imported current documents must use current IDD document shapes. Do not preserve
legacy section layout when the document becomes current normative intent.

Minimum shape for `spec` documents:

```md
# NNNN.spec-short-title

## Intent

Describe the durable product intent.

## Related Specifications

List related specs, ADRs, or spikes that define adjacent, shared, or dependent
intent.

## Behavior

Describe observable behavior and domain contracts.

## Durable Architecture And Constraints

Describe only architecture boundaries and technical constraints that future
implementations must preserve. Include implementation patterns, frameworks, or
libraries only when changing them would change product behavior, compatibility,
public contracts, security, operability, or an accepted architecture decision.

Do not include private class names, private methods, file names, constructor
signatures, dependency-wiring steps, temporary workarounds, migration steps, or
current code structure.

## Non-goals

List behavior or scope that is intentionally excluded.

## Acceptance Criteria

List conditions that must hold for the specification to be satisfied.

## Verification

Describe durable verification properties and evidence required to establish
correctness. State what must be verified, not the local command used to run
verification.

Do not include build commands, test-runner commands, CI commands, test class
names, temporary source scans, or step-by-step execution instructions.
```

Minimum shape for `adr` documents:

```md
# NNNN.adr-short-title

## Status

Proposed | Accepted | Superseded | Rejected

## Context

Describe the decision context.

## Decision

Describe the chosen decision.

## Alternatives Considered

List alternatives and why they were not chosen.

## Consequences

Describe accepted tradeoffs and follow-up constraints.

## Supersedes

Reference the superseded ADR when this ADR replaces an earlier decision.
```

Minimum shape for `spike` documents:

```md
# NNNN.spike-short-title

A spike is active research only while the question is unresolved. When resolved,
move durable product behavior into a spec, move durable architecture decisions
into an ADR, and delete the spike unless it remains useful as active research.

## Question

State the uncertainty or hypothesis being tested.

## Constraints

List constraints for the investigation.

## Method

Describe how the spike is evaluated.

## Result

Record what was learned.

## Recommendation

State the proposed follow-up.
```

If a spike already has `Result` or `Recommendation` and is no longer active
research, import must choose one of these outcomes:

- convert the result into a spec or adr if it defines current product intent;
- delete or skip it if it is only historical research;
- keep it as a spike only if the question is still active and unresolved.

## Relation Normalization

After writing documents, scan all current specs, ADRs, and spikes for numeric
references.

For each reference:

1. Check whether the target file exists.
2. If missing, consult the source-to-target map.
3. Rewrite to the current target when safe.
4. Remove historical-only references.
5. Stop and report a blocking ambiguity only when the relation is
   product-relevant but cannot be safely mapped.

Do not finish with a relation such as:

```text
Related: 0026
```

when `0026` does not exist.

It is valid to rewrite the relation when the source remap proves the target:

```text
Related: 0027
```

It is also valid to preserve traceability as non-normative history:

```text
Source history: extracted from legacy .worklog/0026.
```

Source history is not a normative relation.

## Index Regeneration

Regenerate `.idd/intent/INDEX.md` from actual current numbered documents after
import. Never leave placeholder index content. Never rely on the source index as
the final target index.

Minimum structure:

```md
# IDD Intent Index

| Document | Role | Area | Notes | Replaces |
| --- | --- | --- | --- | --- |
| 0001.spec-product-overview.md | spec | Product overview | ... | |
| 0002.adr-rendering-architecture.md | adr | Rendering | ... | |
| 0003.spike-input-layer-feasibility.md | spike | Input layer | ... | |
```

Rules:

- every current numbered document under `.idd/intent/` must be listed;
- every listed document must exist;
- process reports, templates, README files, and support docs must not be listed
  as current specs;
- there must be no Archived section.

## Workflow

1. Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and relevant existing current
   specs.
2. Read the requested source roots.
3. Discover source methodology and lifecycle conventions.
4. Build the import inventory.
5. Classify fragments into product intent, ADR, spike, historical context,
   process noise, cleanup/refactor notes, wrappers, and conflicts.
6. Build the source-to-target remap.
7. Build a product area map.
8. Perform structural normalization:
   - split oversized or mixed-scope material;
   - merge tiny related fragments into existing areas;
   - extract shared models;
   - separate ADR and spike material;
   - reject task/refactor/cleanup notes as current specs.
9. Propose or infer target files.
10. Write normalized current specs, ADRs, or active spikes according to mode and
   safety.
11. Keep conflicts visible and unresolved.
12. Run post-import cleanup.
13. Return the import report in the assistant response, or write it outside
    `.idd/intent` only when persistent output is explicitly needed.
14. Run relevant repository checks.

## Post-import Cleanup

Before finishing:

1. Delete `.idd/intent/archive` if it exists.
2. Remove import/process reports from `.idd/intent`.
3. Regenerate `.idd/intent/INDEX.md`.
4. Validate and rewrite numeric relations through the source-to-target map.
5. Normalize current specs, ADRs, and spikes to current section shapes.
6. Reclassify resolved spikes.
7. Remove or merge task-like, process-only, duplicate, and historical-only docs.
8. Run or simulate `idd-intent-lint`.
9. Continue fixing mechanical errors until none remain.

## Import Report

Do not create `.idd/intent/import-report.md`.

Import reports are process output, not product intent. They must not be stored
inside `.idd/intent`.

Prefer returning the import report in the assistant response. If a persistent
report is explicitly needed, write it outside `.idd/intent`, for example:

- `.intent-driven-development/import-report.md`;
- `docs/import-report.md`.

The report must not recommend creating `.idd/intent/archive` or link to
`.idd/intent/archive/...`.

Include:

- source roots inspected;
- source methodology detected;
- source files skipped and why;
- source files imported and target documents;
- fragments extracted from task/process documents;
- structural normalization decisions;
- conflicts found;
- obsolete documents skipped or deleted;
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
- `.idd/intent/INDEX.md` is regenerated from actual current numbered documents.
- numeric relations point only to existing current documents.
- no process/import report remains under `.idd/intent`.
- no `.idd/intent/archive` directory exists.
- imported specs, ADRs, and active spikes use current document shapes.
- resolved spikes are converted, removed, or justified as still-active
  research.
- `idd-intent-lint` would return no errors.
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
- update `.idd/intent/INDEX.md`.

### Import legacy `.worklog` with broken historical references

Input:

- old `.worklog` contains numbered documents;
- some documents were archived or obsolete;
- several current documents use `Related` / `Replaces` links to documents that
  are not imported;
- source has old section layouts;
- source contains resolved spikes and task-like cleanup notes.

Expected import behavior:

- do not create `.idd/intent/archive`;
- do not create `.idd/intent/import-report.md`;
- build source-to-target mapping;
- rewrite references to current targets when documents were absorbed or merged;
- remove historical-only relations;
- regenerate `.idd/intent/INDEX.md`;
- normalize specs, ADRs, and active spikes to current shapes;
- convert or remove resolved spikes;
- finish with no `idd-intent-lint` errors.

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
- broad diagnostics of current `.idd/intent` structure without import;
- rewriting specifications just to make them nicer;
- deriving requirements from code;
- automatically resolving product conflicts;
- moving tasks into `.idd/intent/`;
- creating a project plan or implementation backlog.

Use `idd-intent-audit` for broad structural diagnostics without edits. Use
`idd-intent-normalize-current` only for later maintenance of an existing `.idd/intent`
tree, not as a required manual cleanup after import.
