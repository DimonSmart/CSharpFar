---
name: worklog-review
description: Review whether current changes follow the worklog-driven development rules. Use before commits or when checking worklog structure.
---

# Worklog review

Read:

1. `docs/worklog-method.md`
2. relevant `.worklog/*.active.md` files
3. current git diff
4. `AGENTS.md`
5. `CLAUDE.md` if present

Check:

1. Does this change require a work document?
2. If yes, does the document exist?
3. Is the type correct: `spec`, `adr`, or `spike`?
4. Is the numeric sequence correct?
5. Does the document have clear Goal, Context, Done criteria, and Outcome?
6. Does every work document use `.active.md` or `.retired.md`?
7. Are retired documents excluded from current requirements unless referenced through `Replaces` or explicitly needed for history?
8. If an old requirement or ADR changed, was the old active document renamed to `.retired.md` and the new active document given `Replaces:` with the old document number?
9. Are accepted ADRs and old requirements left semantically immutable?
10. Are micro-changes and product-neutral small changes kept out of `.worklog/` and left to commit messages?

Output:

- `OK` if the structure is fine.
- Otherwise list concrete fixes.
- Do not modify files unless the user asks.
