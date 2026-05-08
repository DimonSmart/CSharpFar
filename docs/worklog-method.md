# Worklog-driven development

## Goal

Keep project intent, decisions, experiments, and significant changes in repository files instead of chat history.

The worklog is not a replacement for commits. It records meaningful engineering intent.

Small product-neutral changes belong in commit messages, not in `/.worklog`.

## Directory

All work documents live in `/.worklog`.

Use one increasing numeric sequence:

`NNNN.type-short-title.md`

Examples:

- `0001.spec-initial-mvp.md`
- `0002.spike-console-double-buffering.md`
- `0003.adr-use-console-frame-buffer.md`
- `0004.task-implement-buffered-renderer.md`

Do not split documents into `/specs`, `/tasks`, `/adr`, or `/spikes`. The sequence matters more than classification folders.

## Document Types

### spec

Use `spec` when the system gets a new capability or user-visible behavior changes.

Question answered: what should the system do?

### task

Use `task` for small local changes where the intent is not obvious from the diff.

Question answered: what should be fixed now?

### adr

Use `adr` for architectural or long-lived technical decisions.

Question answered: why did we choose this solution?

Accepted ADRs are immutable. If the decision changes, create a new ADR and mark the old one as superseded.

### spike

Use `spike` for research, experiments, feasibility checks, or technical uncertainty.

Question answered: what should be checked before making a decision?

## When Not To Create A Work Document

Do not create a numbered work document for:

- typo fixes;
- formatting-only changes;
- obvious bug fixes fully explained by the commit message;
- small refactoring with no behavior or architectural meaning;
- dependency patch updates with no project-specific decision.

Also do not create a work document for small changes or fixes that do not affect the product as such: no architecture change, no library choice, no experiment, no new capability, and no user-visible behavior change. Describe those changes at the commit-message level.

## Required Sections

Every work document should have:

- title;
- type;
- status;
- related documents;
- goal;
- context;
- done criteria;
- outcome.

Specs should also record scope and non-goals. Spikes should record result and recommendation.

## Status Values

Use these statuses:

- `draft`
- `active`
- `blocked`
- `done`
- `superseded`
- `rejected`

## Agent Workflow

Before significant implementation:

1. Read the latest `/.worklog` documents relevant to the task.
2. Decide whether a new work document is needed.
3. If needed, create the next numbered document from `/.worklog/_templates`.
4. Confirm scope, non-goals, and done criteria.
5. Implement only the described scope.

After implementation:

1. Run verification commands.
2. Update the `Outcome` section.
3. Record deviations from the plan.
4. Add follow-up items if needed.
5. Do not silently rewrite accepted ADRs.
