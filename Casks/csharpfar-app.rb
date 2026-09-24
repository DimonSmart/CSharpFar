cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.72"
  sha256 arm: "96612c64307fd76698ed414f89c81c5522723c6c32d3ca6d084f1f492590ade9",
         intel: "b7c237c5563f2ed9ef37a0314f9e48179008a92f2413269b95fa00fe55656498"

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
