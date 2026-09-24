class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.71"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.71/CSharpFar-v1.0.71-osx-arm64.tar.gz"
    sha256 "a9f6f8b445843525c000e958664ed55eb0ef2849cc18c2d4911d2f9afda026eb"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.71/CSharpFar-v1.0.71-osx-x64.tar.gz"
    sha256 "80c7994c312c6e0aeb89925fe5739821fd6955712e1da9c3cea6c1eb4a7058b4"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
