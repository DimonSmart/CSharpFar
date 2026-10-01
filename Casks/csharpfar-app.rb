cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  depends_on :macos

  version "1.0.82"
  sha256 arm: "a2699768d8df23a3638cf6aff1f24e0a83a4bba96a75345171f5a109c7ca692a",
         intel: "887ac83356df6951e0cfdc79cee968f016566e9f4a46dd96914ad8f35e755541"

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
