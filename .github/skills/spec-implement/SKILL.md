---
name: spec-implement
description: Implement behavior from current `.specs/` product intent and verify the code against the relevant specification.
---

# spec-implement

Use this skill when the user asks to implement behavior that is already
specified, or when `spec-change` has just updated the relevant spec.

Formula:

```text
spec-implement = current spec intent + code change + verification
```

## Rules

- Current `.specs/` documents are the source of product intent.
- Do not implement durable product behavior that is missing from specs.
- If the request changes product behavior and specs are not updated yet, use
  `spec-change` first.
- Read `.specs/README.md`, `.specs/INDEX.md`, and only relevant current specs.
- Do not read the whole `.specs/` directory by default.
- Do not copy implementation plans or temporary notes into specs.
- Prefer the smallest code change that satisfies the relevant acceptance
  criteria.
- Add or update tests when the behavior can be tested.
- Run relevant verification.
- After implementation, perform a focused implementation/spec check using
  `spec-check-implementation`.

## Workflow

1. Identify the relevant spec and acceptance criteria.
2. Locate the implementation area.
3. Locate existing tests for the behavior.
4. Implement the smallest change that satisfies the spec.
5. Add or update tests.
6. Run relevant verification.
7. Run or recommend focused `spec-check-implementation`.
8. Report:

   - specs used as intent;
   - code areas changed;
   - tests added or updated;
   - verification result;
   - remaining risks or missing coverage.

## Missing Spec Rule

If the requested behavior is not covered by current specs:

```text
Stop before implementation and use spec-change.
```

Do not silently implement new durable behavior without updating product intent
first.

## Example

If `.specs/0018.spec-command-history-completion.md` says command completion must
have a neutral default selection, implement that behavior in command completion
code and tests, then verify the implementation against spec 0018.
