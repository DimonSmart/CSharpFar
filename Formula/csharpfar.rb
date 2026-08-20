class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.62"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.62/CSharpFar-v1.0.62-osx-arm64.tar.gz"
    sha256 "b353f6c1d3d625de4a484a7216613a4cc39eef4ad81411ac600743a5b1682631"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.62/CSharpFar-v1.0.62-osx-x64.tar.gz"
    sha256 "871c0cb5dd300cf00e2ec4b2c3147d28856eec74c3412e04a9adf2e4fe1da4d5"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
