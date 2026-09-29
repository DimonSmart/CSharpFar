class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.78"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.78/CSharpFar-v1.0.78-osx-arm64.tar.gz"
    sha256 "c782a831354db957a12355adba82d79f81a63138ca353a0fe2423c9176e6f682"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.78/CSharpFar-v1.0.78-osx-x64.tar.gz"
    sha256 "099a1ef49e15729dd312c2c0049402cb3c848b929c910ed45fa13a9a7aa71191"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
