cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.117"
  sha256 arm: "d332cc387a81b6343ab9970b2adb71c2a23972c39be0bcf6fa56e45e09387d06",
         intel: "13af60b4e283e454580fadf399d43857899c77e361d7eb702560ea510233f9bb"

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
