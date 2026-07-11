---
name: idd-intent-brainstorm
description: Clarify real product intent before changing `.idd/intent/`, using focused customer-development questions and simplification options without editing product intent, planning implementation, or writing code.
---

# idd-intent-brainstorm

Use this skill before changing `.idd/intent/` when the user describes a product idea,
feature, behavior change, or requirement whose real product intent may be
unclear, over-specified, implementation-shaped, or unnecessarily complex.

This skill clarifies intent. It does not update specifications, design architecture, or implement code.

Formula:

```text
idd-intent-brainstorm = proposed solution + customer discovery + intent clarification + simplification options
```

## Purpose

A user often describes the first solution they imagined, not the underlying
problem they need solved.

This skill helps separate:

1. real product intent;
2. user/customer problem;
3. current workaround;
4. success criterion;
5. minimum useful outcome;
6. proposed solution;
7. accidental complexity.

The goal is to avoid turning an expensive solution idea into durable product
intent before checking whether a simpler product-level outcome would satisfy the
same need.

## When to use this skill

Use this skill when:

- the user asks for a new feature and the product goal is not yet clear;
- the user describes a solution rather than a problem;
- the request contains complexity multipliers such as configurable, dynamic,
  generic, workflow engine, rule engine, plugin system, fully customizable,
  AI-powered, metadata-driven, multi-tenant, extensible, or framework;
- the requested feature may be much more complex than the visible product value;
- there may be a simpler product-level alternative;
- the user is unsure what the product should do;
- the user asks to brainstorm the product behavior or intent;
- the requested behavior may belong in specs, but the exact intent is not ready
  for `idd-intent-change`.

## When not to use this skill

Do not use this skill when:

- the relevant current specification is already clear and the user asks to implement it;
- the user has already made the product decision and asks to update specs;
- the task is only a local refactor, cleanup, dependency update, build fix, or
  implementation detail;
- the request is about checking implementation against current specs;
- the request is about importing, linting, auditing, or normalizing `.idd/intent/`;
- the request is mainly architectural and should be handled as an ADR or spike;
- the user explicitly asks not to discuss product intent.

## Boundaries

`idd-intent-brainstorm` never edits files.

If the user confirms a product direction and asks to persist it, route to
`idd-intent-change` when an existing current document owns the area. Route to
`idd-intent-new-document` when the result is a new durable product area, ADR, or
spike.

It must not:

- update `.idd/intent/`;
- create new specs;
- create ADRs;
- create spikes;
- implement code;
- create implementation plans;
- choose architecture;
- verify implementation against specs;
- normalize existing specs;
- import external source material;
- treat the current implementation as product intent.

The output of this skill may become input for `idd-intent-change`, but only after the
user confirms the intended product direction.

Do not automatically continue into `idd-intent-change`. First present the clarified
intent or simplification proposal and wait for explicit user confirmation.

## Reading Existing Specs

Do not load the whole `.idd/intent/` tree.

If the brainstorm concerns an existing product area, read only enough context to
avoid brainstorming in a vacuum:

1. `.idd/intent/README.md`;
2. `.idd/intent/INDEX.md`;
3. the likely owning current spec, if it can be identified cheaply.

Use existing specs only to understand current product intent and ownership
boundaries.

Do not treat current implementation as product intent.

Do not update specs from this skill.

## Relationship to other skills

Use `idd-intent-brainstorm` before `idd-intent-change` when the desired product intent is not
clear enough to write or update specs.

Use `idd-intent-change` when the user has confirmed a desired product behavior change
and `.idd/intent/` should be updated.

Use `idd-intent-new-document` when a new durable product area, ADR, or spike is
needed and no existing current document owns the area.

Use `idd-code-implement` when current specs already describe the behavior to build.

Use `idd-code-check-implementation` when the task is to compare implementation
behavior with current specs.

Use `idd-code-update-intent` only when implementation behavior already
exists and the user explicitly confirms that it represents current product
intent.

Use `idd-intent-normalize-current` when accepted current specs need focused structural
normalization without changing product meaning.

Use `idd-intent-audit` for broad structural diagnostics over `.idd/intent/`.

Use `idd-intent-lint` for cheap mechanical validation.

Use `idd-intent-import` when raw external source material must become normalized IDD
specs.

## Customer discovery questions

Ask questions that reveal the real problem, not questions that merely validate
the proposed solution.

Prefer questions about actual current or past behavior over hypothetical
preferences.

Useful questions:

```text
- What problem are you trying to solve with this?
- What happens today without this feature?
- Can you describe the last time this problem happened?
- Who experiences the problem?
- How often does this happen?
- How painful is it when it happens?
- What do you do now to work around it?
- What part of the current workaround is most annoying?
- What result would be good enough for the first version?
- What would still be acceptable even if it is not perfect?
- What would make this feature unnecessary?
- If a simpler version solved 80% of the problem, would that be acceptable?
- Is the real goal speed, fewer errors, less manual work, better control, better
  visibility, compliance, or something else?
```

Ask only the smallest number of high-leverage questions needed. Usually ask 3-5
questions, not a full interview.

## Bad questions

Avoid questions that invite the user to approve complexity.

Bad:

```text
- Do you want a configurable workflow engine?
- Should this be fully customizable?
- Would an AI-powered version be useful?
- Should we prepare this for future scaling?
- Do you want a plugin system?
```

Better:

```text
- When did you last need to change this workflow?
- Who would change it?
- How often would it change?
- What happens if changing it requires a developer?
- Is the current problem about flexibility, or about one missing behavior?
- What is the simplest fixed workflow that would work for now?
```

## Problem evidence classification

Classify the request as one of:

```text
observed-problem
expected-problem
hypothetical-problem
solution-preference
unclear-intent
```

Use `observed-problem` when the user has already experienced the problem.

Use `expected-problem` when the user reasonably expects the problem soon.

Use `hypothetical-problem` when the user is preparing for a possible future
situation without current evidence.

Use `solution-preference` when the user mainly describes how they imagine the
feature should be implemented.

Use `unclear-intent` when the real problem or desired outcome is not yet clear.

Observed problems may justify updating specs.

Expected problems may justify a small scoped requirement or extension point.

Hypothetical problems should usually not justify heavy generic infrastructure.

Solution preferences must be translated back into product intent before specs
are changed.

## Workflow

1. Restate the apparent intent in product language.
2. Separate:

   - underlying problem;
   - proposed solution;
   - possible implementation assumption.

3. Ask focused customer-discovery questions when the real intent is unclear.
4. Identify current workaround, frequency, pain level, and success criterion.
5. Classify problem evidence.
6. Identify possible accidental complexity.
7. Propose product-level alternatives:

   - original requested version;
   - simpler useful version;
   - minimal first version.

8. Recommend a specification direction.
9. Stop before editing `.idd/intent/`.
10. Tell the user which follow-up skill should be used next.

## Output formats

### Intent clarification

Use when more information is needed.

```md
# Intent Clarification

## Current Understanding

...

## Proposed Solution Mentioned By User

...

## Possible Underlying Problem

...

## Questions That May Simplify The Requirement

1. ...
2. ...
3. ...

## Next Step

After the product intent is confirmed, use `idd-intent-change` to update `.idd/intent/`.
```

### Intent simplification proposal

Use when the request looks more complex than needed.

```md
# Intent Simplification Proposal

## Original Request

...

## Underlying Intent

...

## Problem Evidence

Classification: `observed-problem | expected-problem | hypothetical-problem | solution-preference | unclear-intent`

Evidence:

...

## Complexity Risk

...

## Options

### Option A - Original Request

What it gives:

...

Cost/risk:

...

When it makes sense:

...

### Option B - Simpler Product Version

What it gives:

...

Cost/risk:

...

What it does not cover:

...

### Option C - Minimal Useful Version

What it gives:

...

Cost/risk:

...

Upgrade path:

...

## Recommended Direction

...

## Next Step

If the user confirms the direction, use `idd-intent-change` for an existing owner
or `idd-intent-new-document` for a new owner, ADR, or spike.
```

### Specification-ready intent

Use when the intent is clear enough to become a spec change.

```md
# Specification-Ready Intent

## Intent

...

## Problem

...

## User / Actor

...

## Current Workaround

...

## Desired Outcome

...

## Minimum Useful Version

...

## Accepted Simplifications

...

## Out Of Scope

...

## Acceptance Criteria Candidate

- ...
- ...
- ...

## Open Questions

- ...

## Handoff To `idd-intent-change`

- Confirmed direction:
- Owning product area:
- Candidate existing spec, if known:
- Minimum useful version:
- Accepted simplifications:
- Explicitly out of scope:
- Open product questions:

## Next Step

Use `idd-intent-change` to update the owning current spec, or
`idd-intent-new-document` if no current document owns the area.
```

## Rules

- Do not jump to code.
- Do not create an implementation plan.
- Do not update `.idd/intent/`.
- Do not discuss architecture unless it is necessary to expose accidental
  complexity.
- Do not automatically continue into `idd-intent-change`.
- Do not assume the user's proposed solution is the real requirement.
- Do not force simplification if the user confirms that the complexity is
  essential.
- Do not ask generic questions when a concrete alternative can be offered.
- Preserve the user's real product goal.
- Prefer small, useful, reversible first versions.
- Treat future flexibility as a cost unless there is a real current need.
- Treat configuration, extensibility, plugins, workflows, generic engines, and
  AI automation as complexity multipliers.
- Ask what outcome would be acceptable, not how the system should be built.

## Examples

### Generic permission engine

User request:

```text
Add a configurable permission engine with custom roles and nested rules.
```

Expected behavior:

- identify that this may be a solution preference;
- ask who needs different permissions and how often rules change;
- propose fixed roles or a narrow permission rule if sufficient;
- do not design the permission engine;
- do not update specs until the user confirms the intended product direction.

### AI categorization

User request:

```text
Use AI to automatically categorize all uploaded documents.
```

Expected behavior:

- ask what problem categorization solves;
- ask how documents are found today;
- distinguish document search, customer grouping, manual tagging reduction, and
  document-type detection;
- propose non-AI alternatives if they satisfy the desired outcome;
- recommend `idd-intent-change` only after the outcome is confirmed.

### Already clear spec

User request:

```text
Implement spec 0018.
```

Expected behavior:

- do not use `idd-intent-brainstorm`;
- use `idd-code-implement`.

### Confirmed behavior change

User request:

```text
Update the command completion spec so Enter no longer accepts the first
suggestion by default.
```

Expected behavior:

- do not use `idd-intent-brainstorm`;
- use `idd-intent-change`.

## Non-goals

This skill does not:

- edit specifications;
- create specifications;
- create ADRs or spikes;
- implement product behavior;
- design architecture;
- compare implementation with specs;
- update specs from implementation;
- normalize or audit `.idd/intent/`;
- perform a broad product discovery process;
- replace user confirmation of product intent.
