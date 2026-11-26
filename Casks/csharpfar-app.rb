cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.129"
  sha256 arm: "0f9925dc9e6329897606cb72cd1b21fc47ef5dd7e13a542eb4b807c8e840295a",
         intel: "91ac6f77c6556ae50aedc9faa8726e16afbcc04d376aab84ff2e30a293c5a126"

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
