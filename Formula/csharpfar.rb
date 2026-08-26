class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.65"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.65/CSharpFar-v1.0.65-osx-arm64.tar.gz"
    sha256 "1f0e3d78d7b90fab8983ce68fd5b52754f827875e532a11adcfec70754b0831d"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.65/CSharpFar-v1.0.65-osx-x64.tar.gz"
    sha256 "ce0fcc1a83c15107ce8584eb86662f33a4ca475b97d0979e870b13e7fb387968"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
