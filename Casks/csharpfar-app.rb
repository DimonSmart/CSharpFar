cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.118"
  sha256 arm: "bbcb18332bae43678adf703d68b86076a98b42d7fc2103ba009b7d0819037b5a",
         intel: "0af78e1e5c226d1948840d6e10f5abe6fdd17a13ec21bb7ff6447193eed0383d"

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
