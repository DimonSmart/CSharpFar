class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.66"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.66/CSharpFar-v1.0.66-osx-arm64.tar.gz"
    sha256 "3fb31bf0232910eeb75195af1205935a776667daa654caebef7ccfb23028eb23"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.66/CSharpFar-v1.0.66-osx-x64.tar.gz"
    sha256 "b0f45d1919a9dc9207349787838d25b9e4c2c962338c95f5a23d5481069afc98"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
