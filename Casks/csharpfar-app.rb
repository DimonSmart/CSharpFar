cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.112"
  sha256 arm: "d8a392ff855071245b917c3c09d53984df9d9dc333852e30f5bfac4f2bf2ce82",
         intel: "6716526fe1aafbb238a4bdf68ff11dfa870399acf6357937fe6b49bc153ad43a"

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
