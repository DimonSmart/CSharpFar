cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.123"
  sha256 arm: "edbb35c3de7b01ab82a9868ed7ce6aa88d863ffffa60cecc553139427f6a9eaa",
         intel: "982efea0a503c22255a840d4b7ac8af8c3cc8618d2fddf09d73bf205ca4cf8d2"

  url "https://github.com/DimonSmart/CSharpFar/releases/download/v#{version}/CSharpFar-v#{version}-osx-#{arch}-app.zip"
  name "CSharpFar"
  desc "Far-inspired terminal file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  app "CSharpFar.app"

  caveats <<~EOS
    CSharpFar.app opens its own native window with an embedded terminal.

    The macOS application is currently unsigned and not notarized. On first launch,
    macOS may require using Open from the Finder context menu.

    For a command-only installation use:
      brew install dimonsmart/csharpfar/csharpfar
  EOS
end
