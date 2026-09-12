# DimonSmart.Terminal package release

This document describes the release procedure for the coordinated `DimonSmart.Terminal` and `DimonSmart.Terminal.Ui` package family. It is intentionally separate from the CSharpFar application release.

## Build-only verification

Run the **Reusable NuGet packages** GitHub Actions workflow with `publish` left disabled.

The workflow:

1. restores, builds, and tests the repository on Windows, Linux, and macOS;
2. runs `eng/verify-reusable-packages.ps1` on every supported OS;
3. creates the coordinated packages from the version in `eng/DimonSmart.Terminal.props`;
4. uploads the resulting package artifacts;
5. does not authenticate to or publish to NuGet.org.

Before the first public release, inspect:

- all reusable public API, which remains in `PublicAPI.Unshipped.txt`;
- both `.nupkg` files and their nuspec metadata;
- package dependencies and packaged DLL/XML/PDB artifacts;
- the package README;
- the package-mode `CSharpFar.Ui.Demo` build.

Do not move API entries to `PublicAPI.Shipped.txt` until the corresponding version has actually been published.

## One-time NuGet.org trusted-publishing setup

The publish job uses NuGet.org Trusted Publishing and GitHub OIDC. No long-lived NuGet API key belongs in the repository.

Configure a Trusted Publishing policy on NuGet.org for:

- repository owner: `DimonSmart`;
- repository: `CSharpFar`;
- workflow file: `nuget-packages.yml`;
- package scopes: both `DimonSmart.Terminal` and `DimonSmart.Terminal.Ui`.

Set the GitHub Actions repository variable `NUGET_USER` to the NuGet.org profile name that owns or is allowed to publish both package IDs. It is a user/profile name, not an email address.

If an environment is added to the publish job later, add the same environment restriction to the NuGet.org trusted-publishing policy.

## First publish

After API and artifact review, manually dispatch **Reusable NuGet packages** with:

- `publish = true`;
- `confirm_version` equal to the exact coordinated package version shown by the build-only workflow.

The workflow first publishes `DimonSmart.Terminal`. It publishes `DimonSmart.Terminal.Ui` only after the terminal package push succeeds.

After the first successful publish, make a separate small change that records the actually published API as the shipped baseline.

## Debugging artifacts

The wrapper packages aggregate separately built implementation assemblies. The normal `.nupkg` therefore includes each packaged DLL together with its XML documentation and portable PDB. The .NET 10 SDK supplies Source Link build support, and release packing records the repository commit in package metadata.

A separate `.snupkg` is deliberately not required for the first beta preparation change. Correct symbol-package generation for this manual aggregate-wrapper scheme should be validated independently before enabling it; do not publish a nominal symbol package that cannot map the packaged assemblies to their real sources.
