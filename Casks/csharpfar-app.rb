cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.131"
  sha256 arm: "c0b86190e26fe9ac60dffb46c58f7c5333154b80fb5871252aa2da0c7a9e6297",
         intel: "c2ed1caa065998a3723c3f00daa4ab11f1aecd436be73ef2330d737c42ef8d4e"

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
