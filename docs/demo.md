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

Demo Mode is the supported source of truth for README and documentation
recordings because it isolates the walkthrough from the user's real file
system and configuration.

The removed VHS-based recording flow is no longer supported. It depended on an
external browser-hosted terminal-video stack, which made reproduction sensitive
to non-product tooling, Linux desktop libraries, terminal rendering quirks, and
environment-specific timing.

The replacement is the product-owned `CSharpFar.DemoRecorder`. It drives the
application through its own input model, captures committed frames from a
recording console driver, and renders repository-owned PNG, GIF, and MP4 demo
artifacts from the same product UI that users see.

Run the recorder manually with:

```powershell
pwsh ./scripts/demo/generate-demo-assets.ps1
```

Or pass explicit paths:

```powershell
pwsh ./scripts/demo/generate-demo-assets.ps1 `
  -Fixture docs/demo/filesystem `
  -Scenario scripts/demo/readme-demo.json `
  -Output artifacts/demo
```

The recorder is intentionally manual. Demo-asset generation is not part of the
default build, test, or publish flow.

Detailed requirements for the recorder and its constraints live in
[`demo/requirements.md`](demo/requirements.md).
