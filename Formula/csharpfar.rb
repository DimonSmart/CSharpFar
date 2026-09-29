class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.77"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.77/CSharpFar-v1.0.77-osx-arm64.tar.gz"
    sha256 "7d7ca15aa01cd3012c68ee0f66d52be91340be2e2bf18e6878de25f4f64f303a"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.77/CSharpFar-v1.0.77-osx-x64.tar.gz"
    sha256 "605e61ce698ae2fb0522b164fa6b7e064432a77d3eff488f2be80d0e61ebf38d"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
