cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.103"
  sha256 arm: "af7c9bc5627c8a656943a94241c2c3a263a134b53b3f728d71f31bd726595e8f",
         intel: "37cefad9f241c53e12f9db26f4faa233219e4735881fff5c9e00764b892daaa0"

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
