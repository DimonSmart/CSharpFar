---
name: idd-skip
description: Manual-only command. Perform the current request without applying Intent-Driven Development routing or updating durable intent.
disable-model-invocation: true
user-invocable: true
---

# idd-skip

Use this skill only when the user explicitly invokes `idd-skip` or explicitly
requests that the current task be performed without Intent-Driven Development.

This is a manual-only command.

## Purpose

Skip IDD routing for the current user request only.

## Rules

- Never select this skill automatically.
- Apply it only to the current request.
- Do not update `.idd/intent/`.
- Do not require missing product behavior to be specified before implementation.
- Do not disable IDD for the project or future requests.
- Do not modify installed packs, project configuration, or CodingAgent entry files.
- Perform the requested task using the repository's normal engineering conventions.
- If the request explicitly asks to inspect or modify IDD files, perform that work;
  skip only the automatic IDD workflow requirement.

## Non-goals

This skill does not remove IDD from the project, change durable product intent,
persist an opt-out setting, or affect later user requests.
