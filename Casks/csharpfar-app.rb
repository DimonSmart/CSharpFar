cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.68"
  sha256 arm: "42250fd43fccafd72dd2e8c3d43152ff4706f40dcb2ce8e68bc6621d2207c60f",
         intel: "646a4f9beefe473966060a199b4a4426e836e6b9f2c20ecada8a7174f901c168"

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
