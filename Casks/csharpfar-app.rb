cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.74"
  sha256 arm: "736bfcaffd2a662f994c2de3e02bbc9e9afa2cac3adfb9c2d03009c04f68b16e",
         intel: "5968362772bdec56bd87068c0753744813d68fbe43396f6b76df1e02d23a90c9"

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
