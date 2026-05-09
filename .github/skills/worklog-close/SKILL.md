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
- follow-up work.

Rules:

- Do not rewrite accepted ADR decisions.
- Do not rewrite old requirements to mean something new.
- Do not change lifecycle by editing a `Status:` field; lifecycle is represented by the `.active.md` or `.retired.md` filename suffix.
- If the tracked requirement or decision changed substantially, retire the old active document and create a new active document with `Replaces:` instead of rewriting the old document.
- Do not hide deviations.
- If implementation differs from the work document, document the difference.
- If there are uncommitted changes, describe them based on the actual diff.
