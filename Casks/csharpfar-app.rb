cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.65"
  sha256 arm: "0ca27fe3b7d1d3042b85722373676a94309eb64e67f77088c9eab61b692acd64",
         intel: "5ef471a884ba99230a4649d54cddc905ba0f14fa770bd33c42f854c1f7678c1d"

  url "https://github.com/DimonSmart/CSharpFar/releases/download/v#{version}/CSharpFar-v#{version}-osx-#{arch}-app.zip"
  name "CSharpFar"
  desc "Far-inspired terminal file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  app "CSharpFar.app"

  caveats <<~EOS
    CSharpFar is a terminal application. Opening CSharpFar.app launches it in Terminal.

    The macOS application is currently unsigned and not notarized. On first launch,
    macOS may require using Open from the Finder context menu.

    For a command-only installation use:
      brew install dimonsmart/csharpfar/csharpfar
  EOS
end
