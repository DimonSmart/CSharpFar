# Demo Mode

Run demo mode with:

```text
csharpfar --demo <fixture-directory>
```

Example:

```text
csharpfar --demo ./docs/demo/filesystem
```

Behavior:

- The fixture directory is read once at startup.
- The runtime file system exists only in memory and uses `/` as its logical root.
- All file changes are discarded when the session exits.
- External shell commands are disabled.
- External file launching is disabled.
- Built-in network modules are disabled.
- Demo session settings and histories are isolated from the user's normal configuration.
