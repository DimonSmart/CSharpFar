class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.63"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.63/CSharpFar-v1.0.63-osx-arm64.tar.gz"
    sha256 "3c9a74a5fe206dfc7fe55486088df787a0277b3f1b3386e6941ad86f50bd714e"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.63/CSharpFar-v1.0.63-osx-x64.tar.gz"
    sha256 "85c76611ebf61a49f5930137af6faade9c697ec3e5c3766300b61038f62c5b9b"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
