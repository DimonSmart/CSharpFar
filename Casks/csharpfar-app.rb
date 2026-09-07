cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.66"
  sha256 arm: "2c4c456155fc6f608028ebc24d75a4d6f47d6099a6fd7881bff2e730c7b8b5eb",
         intel: "8971189b6eb5d5a24595b5c95b05c4a75e2a41d4ad4abc422636f3bc8e9bc495"

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
