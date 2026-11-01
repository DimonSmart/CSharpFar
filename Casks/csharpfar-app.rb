cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.110"
  sha256 arm: "0da1c554481691791100ffc7ed526b0dbab185daaad3c5d8673851902c4d8331",
         intel: "40878c0bff7fbcba00eeea3162ccb046ef7ef8e9d1234423588b501d843b2ff2"

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
