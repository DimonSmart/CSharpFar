---
name: readme-human-style
description: Create, edit, review, or polish README.md and README-like Markdown fragments in a calm, concrete, non-marketing engineering style close to the repository author's voice. Use for README.md, README*.md, README sections in Markdown files, and short documentation fragments intended to be copied into README.
---

# README human style editor

Use this skill when creating, editing, reviewing, or polishing `README.md` files.

## Goal

Make README text clear, useful, and close to the author's personal engineering style.

The goal is not to bypass AI detectors.
The goal is to remove generic LLM writing patterns and preserve a direct, human, technical voice.

## Scope

Apply this skill only to:

- `README.md`
- `README*.md`
- README sections in Markdown files
- short documentation fragments that are intended to be copied into README

Do not apply this skill to:

- source code comments
- API reference generated from code
- changelogs
- legal text
- copied third-party text
- quoted text

## Required references

Before editing README text, read the relevant style files:

- [readme-style.md](../../../docs/style/readme-style.md)
- [llm-antipatterns.md](../../../docs/style/llm-antipatterns.md)
- [readme-review-checklist.md](../../../docs/style/readme-review-checklist.md)
- [examples.md](../../../docs/style/examples.md)

## Author style

Write like an engineer explaining to another engineer.

Preferred style:

- direct
- practical
- calm
- precise
- structured
- low on fluff
- no marketing tone
- no inflated claims
- no decorative wording
- no artificial friendliness
- no long introductions
- no generic "modern world" framing

Typical structure:

1. What this is.
2. What problem it solves.
3. How it works.
4. How to use it.
5. What the limitations are.

Use short explanations where possible.
Prefer concrete examples over abstract promises.

## Strong rules

When editing README text:

- preserve the original meaning
- do not add facts that are not present in the repository
- do not invent benchmarks, guarantees, features, roadmap items, users, companies, or production usage
- do not make the project sound more mature than it is
- do not add marketing claims
- do not add emotional language
- do not use long dash characters
- do not add filler after headings
- do not produce perfectly smooth corporate text
- do not rewrite everything if a smaller edit is enough

## LLM patterns to remove

Remove or rewrite these patterns:

- "In today's fast-paced world..."
- "This project is a powerful solution..."
- "seamlessly"
- "robust"
- "cutting-edge"
- "revolutionary"
- "game-changing"
- "unlock the power of"
- "designed to empower"
- "takes X to the next level"
- "whether you're a beginner or an expert"
- "makes it easy to"
- "with just a few clicks"
- "comprehensive"
- "intuitive"
- "user-friendly"
- "flexible and scalable"
- "modern and efficient"
- "important step"
- "new era"
- "highlights the importance of"

Do not replace these words mechanically.
Rewrite the sentence so it says something concrete.

## README writing rules

### Headings

After a heading, start with useful content immediately.

Bad:

```markdown
## Installation

Installing the project is simple and straightforward.
```

Better:

```markdown
## Installation

Install the package from NuGet:
```

### Intro

The first paragraph must explain what the project is.

Bad:

```markdown
This project helps developers improve their workflow by providing a powerful and flexible solution.
```

Better:

```markdown
This project is a C# library for comparing strings and rendering the difference as Markdown.
```

### Claims

Every claim must be backed by visible project facts.

Bad:

```markdown
Production-ready library for high-performance diff generation.
```

Better:

```markdown
The library builds a line-by-line diff and can render the result as Markdown.
```

### Examples

Prefer examples that show real usage.

Bad:

```markdown
This library is very easy to use.
```

Better:

```csharp
var diff = MarkdownDiff.Compare(oldText, newText);
Console.WriteLine(diff);
```

### Tone

Avoid fake excitement.

Bad:

```markdown
Get started today and unlock the full potential of your workflow!
```

Better:

```markdown
Use it when you need a readable Markdown diff in logs, reports, or generated documentation.
```

## Editing process

When asked to edit README:

1. Read the current README.
2. Identify the target audience and project type from repository files.
3. Check whether the text contains unsupported claims.
4. Remove LLM-like wording.
5. Rewrite only what needs rewriting.
6. Keep useful technical details.
7. Add missing structure only when it helps.
8. Run a final anti-AI pass.

## Final anti-AI pass

Before finishing, check the edited README for:

- generic claims
- marketing tone
- vague benefits
- repetitive sentence rhythm
- unnecessary adjectives
- filler after headings
- headings that promise more than the section delivers
- claims that are not supported by repository contents

Fix these issues silently before returning the final result.

## Output expectations

When editing a README, return:

1. A short summary of what changed.
2. The changed files.
3. Any assumptions or unsupported claims that were removed.
4. The final patch.

Do not explain basic Markdown.
Do not add long commentary.
