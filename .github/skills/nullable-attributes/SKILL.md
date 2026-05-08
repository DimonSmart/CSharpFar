---
name: nullable-attributes
description: Apply C# nullable analysis attributes from System.Diagnostics.CodeAnalysis when `?`, non-nullable declarations, and ordinary flow analysis are not enough. Use for Try-patterns, guard helpers, late initialization, member null-state guarantees, generic nullability, and compiler flow hints around nullable warnings.
---

# Nullable attributes

## When to Use

- The compiler reports a nullable warning that cannot be resolved with `?` annotations and ordinary control flow.
- A `TryGet` or predicate method guarantees non-null on success but the compiler cannot infer that.
- A guard helper always throws on null, so code after the call is definitively non-null.
- A member is initialized by a helper method rather than the constructor, and the compiler treats it as possibly null.
- A generic method returns `default` on failure without changing value-type signatures.

## When Not to Use

- The contract can be expressed with plain `?` annotations and straightforward guard clauses.
- You are using the attribute to silence a warning without genuine evidence of the contract.
- The attribute would describe a different null contract than the actual behavior.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Method or member with nullable warning | Yes | The exact API position where the contract is unclear to the compiler |
| Intended null contract | Yes | Whether the value is guaranteed non-null, may be null, or depends on a return value |

Load `../../../docs/ai-skills/nullable-attributes/references/nullable-attributes.md` for the attribute table and selection notes.

## Workflow

1. Prefer plain type annotations and ordinary guards first.
2. Add nullable attributes only for contracts the compiler cannot infer.
3. Keep the change metadata-only unless behavior changes were explicitly requested.
4. Pick the narrowest attribute that matches the contract.

## Validation

- The attribute is applied to a position where the compiler actually uses it.
- The declared null contract matches real behavior.
- Nullable warnings are reduced without hiding bugs behind fake suppressions.
