class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.82"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.82/CSharpFar-v1.0.82-osx-arm64.tar.gz"
    sha256 "80bde888deeff0a75a92140dc75e0c13a3c320857b1af63739f15baa75b0ce81"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.82/CSharpFar-v1.0.82-osx-x64.tar.gz"
    sha256 "d6723c9fc11d16d7fa5044c8d38d2fe8e9c7cd57d542d767f4fe756a2f27ba64"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
