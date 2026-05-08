---
id: ref-code-review
scope: [codex, copilot]
category: reference
requires: [core-rules]
---
# Code review checklist

- Does the change keep behavior predictable from names and contracts?
- Are side effects, state changes, and responsibilities obvious at the call site?
- Are comments useful and limited to intent or non-obvious constraints?
- Were direct edits preferred over wrappers, adapters, or compatibility shims?
- Are nullability, exceptions, and logging handled explicitly?
- Is async code fully async and is `CancellationToken` passed through?
- Is the diff limited to the stated task?
