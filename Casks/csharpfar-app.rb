cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.122"
  sha256 arm: "2c49852c9e918854c94846397b319e297007a7db657ca34724b8f8c7e9a25591",
         intel: "8589e504070a1702be018887cd3447099b5f9da366fe47ebd88c75068dfa2b56"

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
