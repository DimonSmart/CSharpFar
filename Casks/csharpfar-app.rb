cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.87"
  sha256 arm: "322291263972b65123c92006b34c714c339130db04b3eb45a142e31d85e6b962",
         intel: "b9baebe7f007c4fd902724f1351e6412ac43e59ec78e5c700cdc93ad816e0a03"

  url "https://github.com/DimonSmart/CSharpFar/releases/download/v#{version}/CSharpFar-v#{version}-osx-#{arch}-app.zip"
  name "CSharpFar"
  desc "Far-inspired terminal file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  app "CSharpFar.app"

  caveats <<~EOS
    CSharpFar is a terminal application. Opening CSharpFar.app launches it in Terminal.

    The macOS application is currently unsigned and not notarized. On first launch,
    macOS may require using Open from the Finder context menu.

    For a command-only installation use:
      brew install dimonsmart/csharpfar/csharpfar
  EOS
end
