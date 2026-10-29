cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.106"
  sha256 arm: "82240b9de97387ee9640138a50af2bc5004db39eca5ee8a21d992ba94a89d4f7",
         intel: "d93a4bc6e54a3553a301ef34729a1819c61590d0bd1584bb832be27f298bfc27"

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
