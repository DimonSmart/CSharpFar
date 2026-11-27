cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.130"
  sha256 arm: "c84ac9d5372b79198677456ecd6add5008b75d6bca1324cd20fc74a8ef3536bd",
         intel: "27f0b046a10fccaaa16d668c2ceff36068c68dcd3e137f7c2b1e3bf1a7d83646"

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
