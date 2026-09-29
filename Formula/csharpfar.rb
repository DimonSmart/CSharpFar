class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.79"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.79/CSharpFar-v1.0.79-osx-arm64.tar.gz"
    sha256 "ba2c4ee21fb9dc86d9be92a02f40e5d77ca94b559f2fd5c8c5d7bf82872e6fb3"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.79/CSharpFar-v1.0.79-osx-x64.tar.gz"
    sha256 "f431c4534d055305181e5d42545c39141734afd37f78ba2ce0b6896514be9d1c"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
