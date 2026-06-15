# CSharpFar TerminalProbe

Small Unix terminal input probe for WSL/Linux diagnostics.

Run inside WSL:

```bash
cd /mnt/c/Private/CSharpFar
dotnet run --project tools/CSharpFar.TerminalProbe -- --raw
```

The raw mode uses `cfmakeraw`, `poll(2)`, and `read(2)`, then prints raw bytes,
printable byte names, and the parsed `ConsoleKeyInfo`.

Compare with .NET `Console.ReadKey`:

```bash
dotnet run --project tools/CSharpFar.TerminalProbe -- --console
```

Exit with `Esc` or `Ctrl+C`.
