cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.133"
  sha256 arm: "474e8f1c5ae0338915d65b5f7e83f2357aadece965b80c62709bbaa47e1ee361",
         intel: "0e5a0b44cb864d164e39271bb7787de5758bda49dff683d5d6bdb9117886a654"

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
