# CSharpFar.Plugin.Templates

Install the local package using
`dotnet new install CSharpFar.Plugin.Templates.1.1.0.nupkg`,
then run:

```sh
dotnet new csharpfar-plugin -n My.ExternalPlugin --PluginId my-external-plugin --PluginTitle "My Plugin"
```

Restore the generated project against NuGet.org (once the SDK is published)
or the local SDK feed, then `dotnet publish`. The manifest is included in
the publish output automatically.

Documentation: https://github.com/DimonSmart/CSharpFar/blob/main/docs/native-plugins.md
