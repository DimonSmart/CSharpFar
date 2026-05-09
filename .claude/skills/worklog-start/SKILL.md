---
name: worklog-start
description: Create the next numbered work document before significant implementation work. Use when starting a feature, user-visible behavior change, architecture decision, or spike.
---

# Worklog start

You are helping maintain the project worklog.

Read:

1. `docs/worklog-method.md`
2. `.worklog/README.md` if it exists
3. relevant `.worklog/*.active.md` files
4. the relevant template from `.worklog/_templates/`

Task:

1. Understand the requested change.
2. Decide whether it needs a work document.
3. If it does not need one, explain briefly why.
4. If it needs one:
   - determine the next `NNNN` number;
   - choose one type: `spec`, `adr`, or `spike`;
   - create `.worklog/NNNN.type-short-title.active.md`;
   - fill it using the matching template;
   - keep scope narrow;
   - add explicit non-goals for specs;
   - add done criteria;
   - add verification commands if known.
5. If the new document replaces an existing active document:
   - rename the old file from `.active.md` to `.retired.md`;
   - add `Replaces:` with the old document number to the new document;
   - do not rewrite the semantic content of the retired document.

Rules:

- Do not implement code in this skill unless the user explicitly asks to continue after the document is created.
- Do not create a work document for micro-changes or small product-neutral changes or fixes that are fully captured by a commit message.
- Do not create `task` work documents; small local tasks belong in commit messages unless they are really a `spec`, `adr`, or `spike`.
- Keep typos, formatting, obvious local fixes, small refactoring with no behavior or architecture meaning, dependency patches with no project-specific decision, and changes with no changed requirement out of `.worklog/`.
- When unsure about a small change, do not create a work document by default.
- Use only `.active.md` documents as current context. `.retired.md` files are history.
- Do not invent requirements.
- If important information is missing, mark it as `TBD` instead of guessing.
- Keep the document useful for a coding agent, not just for a human reader.
