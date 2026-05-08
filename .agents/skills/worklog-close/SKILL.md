---
name: worklog-close
description: Update the Outcome section of an existing work document after implementation or investigation. Use when closing or summarizing tracked work.
---

# Worklog close

Read:

1. `docs/worklog-method.md`
2. the work document mentioned by the user
3. current git diff
4. recent commits if needed

Task:

Update the `Outcome` section of the work document.

Include:

- implementation summary;
- files changed;
- verification performed;
- deviations from the original plan;
- follow-up work;
- whether the document is `done`, `blocked`, `superseded`, or still `active`.

Rules:

- Do not rewrite accepted ADR decisions.
- Do not hide deviations.
- If implementation differs from the work document, document the difference.
- If there are uncommitted changes, describe them based on the actual diff.
