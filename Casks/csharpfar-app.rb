cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.73"
  sha256 arm: "b7e8fa7c805efc4fdb40ba5f3842008aaa510ec886a5b15bbd0c04aee5cf2ab6",
         intel: "8bf88323ebadd9e30a248111efbadf20c7021465bd5bf5dca3a78d2b3f5be70c"

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
