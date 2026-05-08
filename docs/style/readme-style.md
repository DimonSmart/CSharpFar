# README style guide

This document defines the preferred README writing style for this repository.

## Main principle

README should explain the project clearly.

It should not sell the project.
It should not imitate corporate documentation.
It should not sound like generic LLM output.

## Voice

Use a calm engineering voice.

Preferred wording:

- "This project does X."
- "Use it when Y."
- "The library reads X and produces Y."
- "The tool expects X."
- "The limitation is Y."

Avoid:

- "This powerful tool empowers developers..."
- "A seamless and intuitive solution..."
- "In the modern development landscape..."
- "Unlock the potential of..."

## Structure

Use this order when it fits the project:

1. Short description.
2. Problem.
3. Installation.
4. Basic usage.
5. Configuration.
6. Examples.
7. Limitations.
8. Development notes.

Do not force all sections if the project is small.

## Introduction

The first paragraph should answer:

- What is this?
- Who is it for?
- What does it do?

Keep it short.

Example:

```markdown
BrowserCommander exposes a browser tab to an LLM through an MCP server. It is useful when an agent needs to inspect a live page, read DOM state, or help debug layout problems.
```

## Problem section

Explain the problem only when it helps.

Bad:

```markdown
Developers today face many challenges when working with complex browser-based applications.
```

Better:

```markdown
LLM agents usually cannot inspect the actual state of an open browser tab. They see code or screenshots, but not the live DOM, selected element, console output, or current page state.
```

## Solution section

Describe the mechanism, not the promise.

Bad:

```markdown
This project bridges the gap between AI and the browser.
```

Better:

```markdown
The extension connects the active browser tab to a local MCP server. The agent can call tools that read page state and return structured data.
```

## Style rules

- Prefer nouns and verbs over adjectives.
- Prefer concrete behavior over benefits.
- Prefer examples over claims.
- Prefer shorter paragraphs.
- Do not hide limitations.
- Do not over-explain obvious steps.
- Do not repeat the heading in the first sentence.
- Do not use long dash characters.
