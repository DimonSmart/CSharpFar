---
name: spec-check-implementation
description: Check whether implementation behavior matches durable product intent in `.specs/`.
---

# spec-check-implementation

Use this skill to check whether the current implementation satisfies current
`.specs/` product intent.

This skill compares implementation evidence with current specifications and
classifies differences. It does not silently change specifications or code.

## Required Input

The request must provide at least one concrete check focus:

1. Implementation focus

   A code area, feature, command, UI flow, API, module, test suite, or behavior to
   check.

   Examples:

   - authentication flow
   - console table mouse behavior
   - background job retry behavior
   - `src/Auth`
   - `LoginForm`
   - `GET /api/users`

2. Specification focus

   A current specification, ADR, section, or product area to verify against.

   Examples:

   - `0003.spec-authentication.md`
   - the console controls specification
   - session expiration behavior
   - validation error behavior

3. Failure focus

   A failing test, build error, runtime problem, bug report, or observed behavior
   that should be checked against current intent.

   Examples:

   - tests fail around OTP login
   - table mouse wheel scrolls selection instead of content
   - password reset works in code but is not mentioned in specs
   - build fails after implementation

## Missing Focus Rule

Do not run this skill if the user only asks to:

- check the whole project;
- review everything;
- fix all problems;
- make implementation match specs generally;
- improve code quality;
- update specs from code.

If no concrete check focus is provided, ask for one:

```text
Please specify what implementation or specification area should be checked:
a code area, a spec, a behavior, a test failure, or an observed mismatch.
```

## Rules

- Use only current numbered documents directly under `.specs/` as normative
  product intent.
- Treat archived documents as historical context only.
- Do not treat implementation as product intent by itself.
- Do not update specifications unless the user explicitly confirms that the
  implementation represents current product intent.
- Do not change code unless the user explicitly asks for implementation changes.
- Do not classify every difference as a bug.
- Do not classify every implementation behavior as a missing spec.
- Preserve the distinction between:

  - implementation bug;
  - missing verification;
  - unclear intent;
  - missing specification;
  - confirmed intent change;
  - intentional non-goal.

- If specification and implementation disagree, report the mismatch and propose
  the smallest safe next step.
- If intent is unclear, ask for confirmation or recommend a spike.
- If the implementation appears correct but verification is missing, recommend
  adding or updating tests.
- If the specification is current and implementation violates it, classify the
  issue as an implementation mismatch.
- If implementation behavior may be desired but is not specified, classify it as
  possible missing intent, not as current product truth.

## Workflow

1. Identify the concrete check focus from the request.
2. If no concrete focus is present, stop and ask for one.
3. Read `.specs/README.md`, `.specs/INDEX.md`, and relevant current numbered
   documents directly under `.specs/`.
4. Inspect the focused implementation evidence:

   - code;
   - tests;
   - build output;
   - runtime behavior;
   - user-provided bug report;
   - logs, when relevant.

5. Compare observed implementation behavior with current product intent.
6. Classify each finding as one of:

   - `matches-spec`;
   - `implementation-mismatch`;
   - `missing-verification`;
   - `missing-spec`;
   - `unclear-intent`;
   - `possible-intent-change`;
   - `non-goal-or-out-of-scope`.

7. For each mismatch, cite the relevant spec section or explain that no current
   spec covers the behavior.
8. Recommend the smallest next step:

   - fix implementation;
   - add or update tests;
   - ask for product intent confirmation;
   - create or update a spec using `spec-create`;
   - update spec from implementation using `spec-update-from-implementation`
     only after explicit confirmation;
   - create a spike if the correct intent requires research.

9. Do not apply fixes unless the user explicitly asks for them.
10. Run relevant verification only when it is safe and appropriate for the
    repository.

## Output Format

Use this structure:

```md
# Implementation Check

## Scope

What implementation or specification area was checked.

## Relevant Current Intent

Current specs and sections used as normative intent.

## Findings

### 1. Finding title

Classification: `implementation-mismatch | missing-verification | missing-spec | unclear-intent | possible-intent-change | matches-spec | non-goal-or-out-of-scope`

Evidence:
- Spec evidence:
- Implementation evidence:

Explanation:

Recommended next step:

## Summary

- Matches:
- Mismatches:
- Missing verification:
- Missing or unclear intent:
- Recommended action:
```

## Classification Rules

### `matches-spec`

Use when implementation behavior satisfies current spec.

Example:

```text
The spec requires session expiration after 30 days. The implementation expires
sessions after 30 days.
```

### `implementation-mismatch`

Use when current spec is clear and implementation violates it.

Example:

```text
The spec says mouse wheel scrolls the table. The implementation changes row
selection instead.
```

Recommended next step:

```text
Fix implementation or tests. Do not update the spec unless the user confirms the
implementation is the intended behavior.
```

### `missing-verification`

Use when implementation appears to satisfy spec, but there is no test or check
that proves it.

Example:

```text
The code appears to support OTP, but no test covers OTP failure retry behavior.
```

Recommended next step:

```text
Add or update verification.
```

### `missing-spec`

Use when implementation contains durable product behavior that is not described
by current specs.

Example:

```text
Password reset exists in implementation, but no current spec describes password
reset behavior.
```

Recommended next step:

```text
Ask whether this behavior is intended product intent. If yes, use spec-create or
spec-update-from-implementation.
```

### `unclear-intent`

Use when the spec is ambiguous or incomplete.

Example:

```text
The spec says "mouse support" but does not define whether wheel changes
selection or scroll position.
```

Recommended next step:

```text
Ask for product decision or create a spike.
```

### `possible-intent-change`

Use when implementation differs from spec and may represent a deliberate product
change, but the user has not confirmed it.

Example:

```text
The spec says sessions expire after 30 days. Implementation uses 7 days. This
may be a security-driven product change, but it is not confirmed.
```

Recommended next step:

```text
Ask for explicit confirmation before updating specs.
```

### `non-goal-or-out-of-scope`

Use when the checked behavior is explicitly excluded or outside current spec
scope.

Example:

```text
The spec explicitly excludes offline mode. Missing offline behavior is not an
implementation bug.
```

## Examples

Good request:

```text
Use spec-check-implementation to verify whether console table mouse behavior
matches the current specs.
```

Good request:

```text
Use spec-check-implementation to check the authentication implementation against
0003.spec-authentication.md.
```

Good request:

```text
Use spec-check-implementation to classify why OTP login tests fail against the
current product intent.
```

Bad request:

```text
Use spec-check-implementation to check the whole project.
```

Response:

```text
Cannot run spec-check-implementation without a concrete check focus.

Specify one of:
- an implementation area;
- a current spec or behavior;
- a failing test, bug report, or observed mismatch.
```

## Relationship To Other Skills

Use `spec-create` when durable product intent needs a new spec, ADR, or spike.

Use `spec-update-from-implementation` only when the user explicitly confirms
that verified implementation behavior represents current product intent.

Use `spec-reorganize` when existing intent should be moved to a better location
without changing meaning.

Use `spec-check-implementation` before those actions when the problem is a
possible mismatch between implementation and current specs.

## Non-Goals

This skill does not:

- fix code automatically;
- update specs automatically;
- create new product intent from implementation;
- review code quality in general;
- search for all possible project problems;
- replace tests;
- replace code review;
- run broad repository audits without focus;
- treat archived specs as current intent.
