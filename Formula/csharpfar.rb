class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.64"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.64/CSharpFar-v1.0.64-osx-arm64.tar.gz"
    sha256 "0258689bbbc91d35879e8d519e2fc84fe3077a748f4c683f493b893e6d6f8f72"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.64/CSharpFar-v1.0.64-osx-x64.tar.gz"
    sha256 "21b4cf6710cdb0ce4c3cd03f2818b6b09f57f62cdfc54d3f280645dbd3c33fc0"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
