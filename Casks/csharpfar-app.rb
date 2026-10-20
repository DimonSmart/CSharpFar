cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.104"
  sha256 arm: "9b3a3814cb0e7a32a5511343437c6b0d2d189161299efc51615482348a878315",
         intel: "72a221e3b76ecc7e9444ee46c63cc94dca941ccdb564ef6e71b32106697c8cd6"

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
