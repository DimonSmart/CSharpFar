class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.123/CSharpFar-v1.0.123-osx-arm64.tar.gz"
      sha256 "02be38678e81769a97f3ef1344d5fe8eb7ebca5bc760afee763127a5a12bcdb3"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.123/CSharpFar-v1.0.123-osx-x64.tar.gz"
      sha256 "2cc4e387b4a1d026af1d172ce24a38c18de2d7d40c488b6647611dd7bc83a649"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
