---
id: ref-public-api
scope: [codex, copilot]
category: reference
requires: [pack-library]
---
# Public API checklist

- Is the API shape clear from names, parameters, and return types?
- Are nullability and exception expectations part of the contract?
- Would this change break existing callers or observable behavior?
- If the API is public, are XML docs concise and complete enough?
- Was compatibility added only when explicitly required?
