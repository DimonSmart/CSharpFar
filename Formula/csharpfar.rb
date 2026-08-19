class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.60"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.60/CSharpFar-v1.0.60-osx-arm64.tar.gz"
    sha256 "be540c166c7d7eab9b9917ec3a51294e562d42cd49584db9f7e0e42c8c7f0aec"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.60/CSharpFar-v1.0.60-osx-x64.tar.gz"
    sha256 "c5d6ff6be643a458813d54c7c804188f9b929f7768a83395a6c6f74d00261df7"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
