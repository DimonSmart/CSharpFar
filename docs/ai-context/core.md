---
id: core-rules
scope: [codex, copilot]
category: context
---
# Core rules

Baseline design principles and general coding rules for this repository.

## Design principles

- Main principle: follow the principle of least surprise. Names, contracts, dependencies, and side effects must be predictable.
- Keep naming explicit and domain-specific. Parameter names must match domain meaning.
- Do not use a bare `id`. Use descriptive names such as `userId` or `orderId`.
- Make side effects, state changes, and expensive work obvious from the contract or type placement.
- Avoid hidden side effects and unrelated responsibilities in the same type or method.
- Keep methods focused. Remove pass-through helpers, trivial overloads, and redundant wrappers.
- Prefer direct edits to existing code over new abstractions.
- Do not add compatibility shims, fallback paths, or duplicate APIs unless compatibility is explicitly required.
- Keep diffs small. Avoid speculative cleanup and future-proofing unrelated to the task.

## General coding rules

- Prefer self-documenting code. Add comments only for intent, constraints, or non-obvious behavior.
- Write comments in English.
- Prefer guard clauses and early returns over nested control flow.
- Return empty collections instead of `null` when absence is not meaningful.
- Fail fast for invalid state and invalid input. Throw precise exceptions instead of hiding bugs with fallbacks.
- Run the repository checks required by the change before finishing.
