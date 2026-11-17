# Sample Extension

This project is an independently built native CSharpFar extension.

1. Restore the project against the published NuGet feed or a local binary SDK feed
   containing `CSharpFar.Plugin.Abstractions` and `CSharpFar.Core` version 1.0.0.
2. Run `dotnet publish -c Release -o publish`.
3. Copy the entire `publish` folder (including `plugin.json`, the DLL,
   `.deps.json` and any private dependencies) to
   `<config-directory>/extensions/sample-extension/`.
4. Restart CSharpFar and use the **Plugins** menu or enter
   `sample-extension` in its command line.

Extensions run in the CSharpFar process with full user privileges.
The host does not sandbox native plugins.
