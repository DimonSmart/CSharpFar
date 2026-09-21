class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.70"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.70/CSharpFar-v1.0.70-osx-arm64.tar.gz"
    sha256 "bcf3bb34de577d707944a5d53b9db7b795e1c4dd33a4b6cf5d130d599ad6e429"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.70/CSharpFar-v1.0.70-osx-x64.tar.gz"
    sha256 "54c42dde5578a05a98702cdc8ece2dc3c19bd2ee46a0267bdf35b11e95834315"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
