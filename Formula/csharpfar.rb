class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.76"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.76/CSharpFar-v1.0.76-osx-arm64.tar.gz"
    sha256 "82e30873004c2868e918c74f939aa4f614aaa49232dce0d75381faca876fa44a"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.76/CSharpFar-v1.0.76-osx-x64.tar.gz"
    sha256 "5ad51ff5c314e8afe036f8c2ea5c2a06529a25cdcab7041d2c86ef9a75a44286"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
