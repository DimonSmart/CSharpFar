cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.89"
  sha256 arm: "f632b50638070bbe7b81e63095c30f6cb63bf2c57afbfd64694171dd621ca10b",
         intel: "f423cd91d87c1b57fc9332800db9d1fb942d148a216fc2534e79847a51e8cc87"

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
