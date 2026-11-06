class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.118/CSharpFar-v1.0.118-osx-arm64.tar.gz"
      sha256 "af5356c421c4c584bf3add9f3cc464f9cf643ef8cc357ace895371a03d88c268"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.118/CSharpFar-v1.0.118-osx-x64.tar.gz"
      sha256 "3ec221cb1f22ebedc034cf9326589571aade447fe172d733113ff5aaaeb666c3"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
