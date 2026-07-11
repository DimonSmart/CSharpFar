---
name: idd-code-implement
description: Implement behavior from current `.idd/intent/` product intent and verify the code against the relevant specification.
---

# idd-code-implement

Use this skill when the user asks to implement behavior that is already
specified, or when `idd-intent-change` has just updated the relevant spec.

Formula:

```text
idd-code-implement = current spec intent + code change + verification
```

## Rules

- Current `.idd/intent/` documents are the source of product intent.
- Do not implement durable product behavior that is missing from specs.
- If the request changes product behavior and specs are not updated yet, use
  `idd-intent-change` first.
- Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and only relevant current specs.
- Do not read the whole `.idd/intent/` directory by default.
- Do not copy implementation plans or temporary notes into specs.
- Prefer the smallest code change that satisfies the relevant acceptance
  criteria.
- Add or update tests when the behavior can be tested.
- Run relevant verification.
- After implementation, perform a focused implementation/spec check using
  `idd-code-check-implementation`.

## Workflow

1. Identify the relevant spec and acceptance criteria.
2. Locate the implementation area.
3. Locate existing tests for the behavior.
4. Implement the smallest change that satisfies the spec.
5. Add or update tests.
6. Run relevant verification.
7. Run or recommend focused `idd-code-check-implementation`.
8. Report:

   - specs used as intent;
   - code areas changed;
   - tests added or updated;
   - verification result;
   - remaining risks or missing coverage.

## Missing Spec Rule

If the requested behavior is not covered by current specs:

```text
Stop before implementation and use idd-intent-change.
```

Do not silently implement new durable behavior without updating product intent
first.

## Relationship to Factory

`idd-code-implement` implements one focused behavior from current specs.

It does not create or execute Factory Work Plans.
When used from factory execution, the factory task brief is only the local task
scope.
The normative product intent still comes from `.idd/intent/`.

Factory may sequence tasks and reviews, but it must not redefine implementation
rules.

Do not expand a factory task into adjacent work unless required by the relevant
spec.
Report changed files, tests, verification, and concerns back to the factory
workflow.

## Example

If `.idd/intent/0018.spec-command-history-completion.md` says command completion must
have a neutral default selection, implement that behavior in command completion
code and tests, then verify the implementation against spec 0018.
