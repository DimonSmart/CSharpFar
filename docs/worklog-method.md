# Worklog-driven development

## Goal

Keep project intent, decisions, experiments, and significant changes in repository files instead of chat history.

The worklog is not a replacement for commits. It records meaningful engineering intent.

Small product-neutral changes belong in commit messages, not in `/.worklog`.

## Directory

All work documents live in `/.worklog`.

Use one increasing numeric sequence:

`NNNN.type-short-title.lifecycle.md`

Examples:

- `0001.spec-initial-mvp.active.md`
- `0002.spike-console-double-buffering.active.md`
- `0003.adr-use-console-frame-buffer.retired.md`

Do not split documents into `/specs`, `/adr`, or `/spikes`. The sequence matters more than classification folders.

## Document Types

### spec

Use `spec` when the system gets a new capability or user-visible behavior changes.

Question answered: what should the system do?

### adr

Use `adr` for architectural or long-lived technical decisions.

Question answered: why did we choose this solution?

ADRs are immutable once they describe an accepted decision. If the decision changes, create a new ADR and retire the old one.

### spike

Use `spike` for research, experiments, feasibility checks, or technical uncertainty.

Question answered: what should be checked before making a decision?

## When Not To Create A Work Document

Do not create a numbered work document for:

- micro-changes;
- typo fixes;
- formatting-only changes;
- obvious bug fixes fully explained by the commit message;
- small refactoring with no behavior or architectural meaning;
- dependency patch updates with no project-specific decision.

Also do not create a work document for small changes or fixes that do not affect the product as such: no architecture change, no library choice, no experiment, no new capability, no changed requirement, and no user-visible behavior change. Describe those changes at the commit-message level.

If the change is completely understandable from a diff and a commit message, keep it out of `.worklog`.

When unsure whether a small change deserves a work document, do not create one by default. Create a document only when the change needs durable requirements, decision history, or investigation notes.

There is no `task` work document type. Small local tasks stay in commit messages unless they are really a `spec`, `adr`, or `spike`.

## Active And Retired Documents

Worklog documents use lifecycle suffixes in filenames:

- `.active.md` - current source of requirements, decisions, or constraints.
- `.retired.md` - historical document. Do not use it as current requirements.

There are no committed `draft`, `changed`, `deleted`, `outdated`, `superseded`, or `archived` work documents. Drafts live outside the main branch or remain uncommitted.

The agent must use only `.active.md` files as the default work context.

The agent may read `.retired.md` files only when:

- a current document references them through `Replaces`;
- the user explicitly asks to inspect history;
- the task is to explain why a decision was made.

When an active document needs substantial semantic changes:

1. Rename the old file from `.active.md` to `.retired.md`.
2. Create one or more new `.active.md` documents with new numbers.
3. In each new document, add `Replaces:` with the old document number.
4. Do not rewrite the semantic content of the retired document.

Minor factual completion of an active work document is allowed only when it records the execution of that same document, for example updating `Outcome` after implementation. Do not change old requirements in place to mean something new.

If a feature is removed from scope, retire the old feature spec and create a new active spec describing the current requirement that the feature is out of scope.

References use document numbers only:

```md
Replaces:
- 0008

Related:
- 0002
- 0007
```

Do not reference work documents by filename in `Replaces` or `Related`; filenames can change when lifecycle changes.

## Required Sections

Every work document should have:

- title;
- type;
- related documents;
- goal;
- context;
- done criteria;
- outcome.

Specs should also record scope and non-goals. Spikes should record result and recommendation.

When a new document replaces an older one, it must include:

- `Replaces`.

## Agent Workflow

Before significant implementation:

1. Read active `/.worklog/*.active.md` documents relevant to the task.
2. Decide whether a new work document is needed.
3. If needed, create the next numbered document from `/.worklog/_templates`.
4. Confirm scope, non-goals, and done criteria.
5. Implement only the described scope.

After implementation:

1. Run verification commands.
2. Update the `Outcome` section.
3. Record deviations from the plan.
4. Add follow-up items if needed.
5. Do not silently rewrite existing requirements or accepted ADRs.
