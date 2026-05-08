# Nullable attributes reference

When a simple `?` annotation cannot express the null contract, use attributes from `System.Diagnostics.CodeAnalysis`:

| Attribute | Use case |
|-----------|----------|
| `[NotNullWhen(true/false)]` | For `TryGet` or predicate patterns where the argument is non-null when the method returns the specified bool. Also useful on `Equals(object? obj)` overrides when returning `true` guarantees a non-null argument. |
| `[MaybeNullWhen(true/false)]` | For generic `out` parameters that may be `default` on failure without changing value-type signatures to `Nullable<T>`. |
| `[NotNull]` | A nullable parameter or member is guaranteed non-null when the method returns. Useful for guard helpers such as `ThrowIfNull`. |
| `[MaybeNull]` | A non-nullable generic return may still produce `default`. Use sparingly when `T?` would change the signature incorrectly. |
| `[AllowNull]` | A non-nullable property or field accepts null on input but normalizes or replaces it. |
| `[DisallowNull]` | A nullable member should not be explicitly assigned null. |
| `[MemberNotNull(nameof(...))]` | A helper method guarantees specific members are non-null after it returns. |
| `[NotNullIfNotNull("paramName")]` | The return is non-null when the named parameter is non-null. |
| `[DoesNotReturn]` | The method always throws, so code after the call is unreachable. |

Add `using System.Diagnostics.CodeAnalysis;` where needed.

## Selection notes

- Prefer plain nullable annotations and obvious control flow before adding attributes.
- Use attributes to describe contracts, not to silence warnings without evidence.
- For non-generic `Try` methods, prefer nullable `out` parameters plus `[NotNullWhen(true)]`.
- For generic `Try` methods, prefer `[MaybeNullWhen(false)] out T result` to avoid changing value-type signatures.
- For late initialization, combine `= null!` with `[MemberNotNull]` when one method establishes the invariant.
- Verify that the target position supports the chosen attribute. Some misapplied attributes are silently ignored by the compiler.
