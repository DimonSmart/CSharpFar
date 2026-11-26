cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.127"
  sha256 arm: "c0ec55ad7173a77c25558cb2f36d2c0e3b38041cb26bae384c3f0fcc15cbe8d0",
         intel: "94c662ac0cdf088d2db3919679d95481c2a47b5a9d71002f2ba7964ab995e39e"

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
