class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.73"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.73/CSharpFar-v1.0.73-osx-arm64.tar.gz"
    sha256 "337b927eeb60a2492e539e1d5d02e38437d81b42b074a1ccfc49a81fc6785e57"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.73/CSharpFar-v1.0.73-osx-x64.tar.gz"
    sha256 "f3dd04f5e9fcbf771ddbbd8ec7ca13c0023916b0f5b74a0e7cd792dc98dc2d13"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
