---
name: worklog-import
description: Import existing specs/docs/planning documents from an explicitly provided SOURCE_FOLDER into .worklog. Use only when invoked manually by the user as /worklog-import SOURCE_FOLDER. SOURCE_FOLDER is required; if it is missing, ask for it and do not inspect or move source files.
---

# Worklog import

You are helping migrate existing planning documents into the project worklog.

This skill is a manual command:

```text
/worklog-import <SOURCE_FOLDER>
```

It has side effects: it may rename or move files. Do not run it automatically.

## Required argument

This command requires one positional argument:

- `SOURCE_FOLDER`: path to the folder to import.

The first positional argument is the only source folder.

`SOURCE_FOLDER` is mandatory.

Before doing any work, validate that `SOURCE_FOLDER` is present, non-empty, and clearly looks like a folder path or folder name.

If `SOURCE_FOLDER` does not exist or is inaccessible, respond with:

```text
The specified folder does not exist or cannot be accessed. Please provide a valid folder path.
```

Valid usage examples:

```text
/worklog-import docs/specs
/worklog-import docs/planning
/worklog-import old-specifications
```

If the folder path contains spaces, the user must quote it:

```text
/worklog-import "docs/old specs"
```

Invalid usage examples:

```text
/worklog-import
/worklog-import into .worklog
/worklog-import migrate docs/specs into .worklog
```

In the last invalid example, `migrate` would be parsed as the first positional argument, so it must not be accepted as `SOURCE_FOLDER`.

If `SOURCE_FOLDER` is missing, empty, ambiguous, or only implied, ask the user to provide it and stop.

Use this exact response pattern:

```text
Please provide SOURCE_FOLDER, for example: /worklog-import docs/specs
```

Do not scan, classify, rename, move, or edit any files until `SOURCE_FOLDER` is explicitly provided.

## Argument rules

**Source folder identification:**
- Treat `$SOURCE_FOLDER` as the only source folder.
- `SOURCE_FOLDER` must be explicitly provided by the user as the first positional argument.
- Do not infer `SOURCE_FOLDER` from nearby folders, common folder names, repository structure, previous context, existing `.worklog` files, or the command name.

**Handling ambiguity:**
- Do not treat `.worklog` as `SOURCE_FOLDER` when mentioned only as the destination.
- If `$SOURCE_FOLDER` is `.worklog`, ask the user to provide the source folder and stop (unless user explicitly says they want to inspect already imported worklog documents).
- If the user provides more than one possible source folder and the intended one is unclear, ask which folder is `SOURCE_FOLDER` and stop.

**Extra arguments:**
- Optional text after the first argument may be treated as extra instruction, but it must not change the source folder.

## Validation and setup

After `SOURCE_FOLDER` is explicitly provided:

1. Validate that `SOURCE_FOLDER` is accessible and contains supported documents.
2. If invalid, stop and report the error.
3. If valid, create `.worklog/` if it does not exist (this initial setup is permitted).
4. Then proceed to the Read phase.

## Read

After validation and setup, read:

1. `docs/worklog-method.md`
2. `.worklog/README.md` if it exists
3. existing numbered files in `.worklog/`
4. the folder specified by `SOURCE_FOLDER`

If `docs/worklog-method.md` does not exist, report that the methodology file is missing and continue only with the available `.worklog` conventions.

## Task

1. Inventory supported source documents from `SOURCE_FOLDER`.
2. Include Markdown files by default.
3. Include plain text files only if the user explicitly asks to import plain text or `.txt` files.
4. Ignore generated files, binaries, images, archives, build artifacts, hidden dependency folders, and nested dependency folders.
5. Determine the next `NNNN` number after existing numbered `.worklog` documents.
6. For each source document, choose one type: `spec`, `adr`, or `spike`.
7. Create a short kebab-case title from the existing file name or document heading.
8. Rename or move each document to `.worklog/NNNN.type-short-title.active.md` unless the user explicitly says it is historical.
9. Preserve ordering by prioritizing explicit source numbering first, followed by dates. If neither is present, use stable path sort.
10. Report the old path to new path mapping.

## Supported source documents

Include by default:

- `*.md`
- `*.markdown`

Include only when explicitly requested by the user:

- `*.txt`

Ignore:

- generated files
- binaries
- images
- archives
- build artifacts
- dependency folders such as `node_modules`, `.nuget`, `packages`, `bin`, `obj`, `dist`, `build`, `.git`
- temporary files
- editor backup files
- files that are clearly not planning, specification, decision, or research documents
- small local task notes or fixes that belong in commit messages

## Classification

Use exactly one of these document types:

- `spec`: feature specs, requirements, behavior descriptions, or user-visible capability plans.
- `adr`: decisions, alternatives, tradeoffs, accepted approaches, or library choices.
- `spike`: research notes, experiments, feasibility checks, or open technical questions.

If a type cannot be determined confidently, leave the file in place and ask the user for classification.

Do not invent a classification only to complete the import.

## Naming

Target file name format:

```text
.worklog/NNNN.type-short-title.lifecycle.md
```

Where:

- `NNNN` is a four-digit sequence number.
- `type` is one of `spec`, `adr`, or `spike`.
- `short-title` is a kebab-case label; `type` and `short-title` are joined by a hyphen.
- `lifecycle` is `active` or `retired`.
- The extension is `.md`.

Use `.active.md` for current requirements, decisions, and investigations. Use `.retired.md` only for documents the user explicitly identifies as historical or replaced.

Examples:

```text
.worklog/0007.spec-console-rendering.active.md
.worklog/0008.adr-double-buffering.active.md
.worklog/0009.spike-terminal-flickering.retired.md
```

## Ordering

Preserve source order using this priority:

1. Explicit numeric prefixes in file names.
2. Explicit dates in file names.
3. Document headings that contain sequence numbers or dates.
4. Stable path sort.

Do not reorder documents based on guessed importance.

## Move rules

- Do not overwrite existing `.worklog` files.
- If a target path already exists, keep the sequence number unique and choose the next available `NNNN`.
- If two documents would produce the same short title, keep both by preserving their sequence numbers.
- Do not add vague suffixes unless needed to avoid a path collision.
- If a source document is already correctly named and already located in `.worklog`, leave it unchanged.
- Prefer `git mv` for tracked files.
- Use normal filesystem move only for untracked files.
- Do not edit document content unless the user explicitly asks to normalize sections.
- Do not create numbered work documents for product-neutral small changes that belong in commit messages.
- Do not create numbered work documents for micro-changes or changes fully explained by the diff and commit message.

## Safety rules

**Source folder validation:**
- `SOURCE_FOLDER` is required.
- `SOURCE_FOLDER` must be explicitly provided by the user as the first positional command argument.
- Do not infer `SOURCE_FOLDER`.

**File operations:**
- Do not scan, classify, rename, move, or edit any source documents before `SOURCE_FOLDER` is explicitly provided.
- Do not move files outside the repository root unless the user explicitly provided an external path and confirmed that it should be used.
- Do not delete source documents.
- Do not overwrite files.
- Do not modify file contents unless the user explicitly asks for content normalization.

**Reporting:**
- Report every moved, skipped, and unresolved document.

## Missing input behavior

If the user does not provide `SOURCE_FOLDER`, respond only with:

```text
Please provide SOURCE_FOLDER, for example: /worklog-import docs/specs
```

Do not continue the import workflow until the user provides `SOURCE_FOLDER`.

## Final report

After completing the import, report:

1. Imported documents.
2. Old path to new path mapping.
3. Skipped documents and the reason.
4. Documents that need manual classification, if any.
5. The next available `NNNN` number after the operation.

Use a compact table for path mappings.
