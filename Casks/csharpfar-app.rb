cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.108"
  sha256 arm: "5637784c9e47b1217aa8da37640e75c8bbbe4fcd93744f66e06c5d41125f4eb6",
         intel: "2293608440cf893f26b83d24c85223266c767d0dd19771482ab8e8def28cda17"

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
