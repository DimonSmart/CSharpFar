class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.129/CSharpFar-v1.0.129-osx-arm64.tar.gz"
      sha256 "0490019efbb35d8f268926ae133774bf204763f6982dae476fb95cb14e42f46d"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.129/CSharpFar-v1.0.129-osx-x64.tar.gz"
      sha256 "70f902c953f0181cdbe5c971bc112920a228e5147c6d66d7802c3dc72e5cd610"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
