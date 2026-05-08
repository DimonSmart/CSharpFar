---
id: dotnet-rules
scope: [codex, copilot]
category: context
requires: [core-rules]
applies-to: "**/*.cs"
---
# C# and .NET rules

- Use `System.Text.Json` for JSON.
- Keep nullability explicit. If a value can be missing, model it with nullable types and handle it directly.
- Use structured logging with `ILogger`.
- Prefer modern C# features when they reduce ceremony without hiding types, flow, or ownership.
- Use `IReadOnlyCollection<T>` when callers should not mutate returned collections.
- Async methods must end with `Async`.
- Accept `CancellationToken` in async public methods and pass it through to dependencies.
- Do not block on async code with `.Result` or `.Wait()`.
- Do not start fire-and-forget tasks without explicit ownership, cancellation, and logging.
