# Demo Mode

Demo Mode exists for safe, reproducible product walkthroughs. CSharpFar imports
the fixture once, exposes it as the logical root `/`, and discards every
runtime mutation when the session ends.

## Manual run

Run demo mode with:

```text
csharpfar --demo <fixture-directory>
```

Example:

```text
csharpfar --demo ./docs/demo/filesystem
```

## Isolation model

- The fixture directory is read once at startup.
- The runtime file system exists only in memory and uses `/` as its logical root.
- All file changes are discarded when the session exits.
- External shell commands are disabled.
- External file launching is disabled.
- Built-in network modules are disabled.
- Demo session settings and histories are isolated from the user's normal configuration.

The physical fixture is never the live runtime state. Viewer, editor, copy,
rename, and other panel operations work only against the imported in-memory
snapshot.

## Fixture layout

The canonical README demo fixture lives in [`demo/filesystem`](demo/filesystem).
It models an AI-course professor workspace with:

- lecture drafts in `01-Lectures/`;
- incoming and reviewed student submissions in `02-Assignments/`;
- grading criteria in `02-Assignments/03-Rubrics/`;
- research notes in `03-Research/`;
- archived course notes in `04-Archive/`.

## Documentation recording

Demo Mode is the supported source of truth for future automated README
recordings because it isolates the walkthrough from the user's real file
system and configuration.

The removed VHS-based recording flow is no longer supported. It depended on an
external browser-hosted terminal-video stack, which made reproduction sensitive
to non-product tooling, Linux desktop libraries, terminal rendering quirks, and
environment-specific timing.

The intended replacement is a product-owned scripted recorder that drives the
application through its own input model and captures committed frames from a
fake or recording console driver. That design keeps the demo deterministic and
also enables saving real rendered screenshots from those committed frames for
documentation and future visual test evidence.

Until that recorder exists, this directory only defines the demo fixture and
manual Demo Mode entry point.
