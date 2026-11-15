# Independent Hello World extension

This project references **binary NuGet contracts only**, not the CSharpFar source projects.
From the repository root:

```pwsh
./eng/pack-plugin-sdk.ps1
dotnet restore samples/CSharpFar.Plugin.HelloWorld/CSharpFar.Plugin.HelloWorld.csproj --source artifacts/plugin-sdk
dotnet publish samples/CSharpFar.Plugin.HelloWorld/CSharpFar.Plugin.HelloWorld.csproj -c Release --no-restore -o artifacts/hello-world
```

Copy `plugin.json` to `artifacts/hello-world`, then copy the entire directory to
`<config-directory>/extensions/hello-world`. The application's normal startup
loads it automatically. Use the Plugins menu or enter `hello` in its command line.
Counter and disposal marker live in `<config-directory>/plugins/hello-world/`.
See [the native plugin guide](../../docs/native-plugins.md).
