cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.75"
  sha256 arm: "1c786cf4889a27d2fe3ec8d345bebb5dee184bbc6372c59628dd026e4a4ace13",
         intel: "65ab2f021d4b00d6cbcd3669d5562f33c04233b9fa295a0a2b32edf68dfc6c26"

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
