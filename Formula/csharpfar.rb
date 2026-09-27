class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.74"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.74/CSharpFar-v1.0.74-osx-arm64.tar.gz"
    sha256 "5af2bdfca85a4514c1c3f4f8076d7ac08133f348d140691855f848fc7c071070"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.74/CSharpFar-v1.0.74-osx-x64.tar.gz"
    sha256 "3dbc7f74d764b64328a57f9bafbae9a9910de3216493d9800a182886698609da"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
