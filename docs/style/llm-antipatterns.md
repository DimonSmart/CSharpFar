# LLM writing anti-patterns

Use this checklist when reviewing README text.

## 1. Inflated significance

Bad:

```markdown
This marks an important step toward a new era of developer productivity.
```

Fix:

```markdown
This removes one manual step from the review workflow.
```

## 2. Vague attribution

Bad:

```markdown
Many developers struggle with this problem.
```

Fix:

```markdown
This problem appears when the agent needs repository context that is not present in the diff.
```

## 3. Marketing adjectives

Bad:

```markdown
A powerful, flexible, and user-friendly tool.
```

Fix:

```markdown
A command-line tool that reads a diff and produces review comments.
```

## 4. Empty benefit

Bad:

```markdown
It improves your workflow and saves time.
```

Fix:

```markdown
It avoids opening each changed file manually when checking Markdown links.
```

## 5. Filler after heading

Bad:

```markdown
## Usage

Using the tool is simple.
```

Fix:

```markdown
## Usage

Run:
```

## 6. Fake balance

Bad:

```markdown
Whether you are a beginner or an experienced developer, this tool helps you...
```

Fix:

```markdown
Use it when you need to run the same review rules on every pull request.
```

## 7. Over-smooth corporate tone

Bad:

```markdown
This solution enables teams to streamline their development lifecycle.
```

Fix:

```markdown
The tool runs review rules before the pull request is merged.
```

## 8. Unsupported maturity claims

Bad:

```markdown
Production-ready and enterprise-grade.
```

Fix:

```markdown
The project is experimental. APIs may change.
```

## 9. Decorative transitions

Bad:

```markdown
With this in mind, let's dive into the features that make this project unique.
```

Fix:

Remove the sentence.

## 10. Pseudo-depth endings

Bad:

```markdown
This provides a solid foundation for future innovation.
```

Fix:

```markdown
The current version supports Markdown and SQL rules. C# rules are planned separately.
```
