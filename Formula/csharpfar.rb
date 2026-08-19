class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.61"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.61/CSharpFar-v1.0.61-osx-arm64.tar.gz"
    sha256 "6422053f7305118cd737e0fccd1574aa53badc4361bd05a66b8df83f93317470"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.61/CSharpFar-v1.0.61-osx-x64.tar.gz"
    sha256 "af1d02705dbdcdf7f15405d328dcc7dd44e86af1bb3ef808fb0e207c0f775239"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
