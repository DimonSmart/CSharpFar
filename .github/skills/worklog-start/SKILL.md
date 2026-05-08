---
name: worklog-start
description: Create the next numbered work document before significant implementation work. Use when starting a feature, non-trivial fix, architecture decision, or spike.
---

# Worklog start

You are helping maintain the project worklog.

Read:

1. `docs/worklog-method.md`
2. `.worklog/README.md` if it exists
3. the latest 5 numbered files in `.worklog/`
4. the relevant template from `.worklog/_templates/`

Task:

1. Understand the requested change.
2. Decide whether it needs a work document.
3. If it does not need one, explain briefly why.
4. If it needs one:
   - determine the next `NNNN` number;
   - choose one type: `spec`, `task`, `adr`, or `spike`;
   - create `.worklog/NNNN.type-short-title.md`;
   - fill it using the matching template;
   - keep scope narrow;
   - add explicit non-goals for specs;
   - add done criteria;
   - add verification commands if known.

Rules:

- Do not implement code in this skill unless the user explicitly asks to continue after the document is created.
- Do not create a work document for small product-neutral changes or fixes that are fully captured by a commit message.
- Keep typos, formatting, obvious local fixes, small refactoring with no behavior or architecture meaning, and dependency patches with no project-specific decision out of `.worklog/`.
- Do not invent requirements.
- If important information is missing, mark it as `TBD` instead of guessing.
- Keep the document useful for a coding agent, not just for a human reader.
