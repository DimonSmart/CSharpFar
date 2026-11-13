cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.120"
  sha256 arm: "c8586720674c0230b7a9e5e69ae7e88871c2c4dec2570f6fdbf879e6af8510e1",
         intel: "a888e3abb3e80376a99fe4fc08df5c34700f5118b2bc1cc5a54882160897acbf"

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
