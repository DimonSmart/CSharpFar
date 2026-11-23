cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.126"
  sha256 arm: "d036cf6041ec7d8d291b4c37a4d6979c3f2dc709541747649d5f6cb4e4afc98d",
         intel: "230d549f853dc4b249fda84cd24e6c2251b10296aa8ab9cb4bb0a1236c38c265"

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
