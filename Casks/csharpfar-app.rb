cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.71"
  sha256 arm: "22a06adc237e240b0f54c6721e19866654e9d44f16a2289fca7b0d1136ab0308",
         intel: "24784eec1d5b7c0c59d4984e797cdc2acc92a8502b3a77550ca7a88e0f1a677e"

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
