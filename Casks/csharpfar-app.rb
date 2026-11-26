cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.128"
  sha256 arm: "66d18474c908d48da1000d02c0c21c7f631ee405ad5bd1fcefa3270657e970fa",
         intel: "b62e7414fc3be60687be2cfe80fa82b3131ee46ffe218733fde150ec00552792"

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
