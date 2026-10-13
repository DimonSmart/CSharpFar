class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.96/CSharpFar-v1.0.96-osx-arm64.tar.gz"
      sha256 "d2342dab7a628e6223b9b1e1e20cbf15220ca6a5b29fe39b852b5a599b4e0a8a"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.96/CSharpFar-v1.0.96-osx-x64.tar.gz"
      sha256 "b477a68dc745a8659609c076f7fbc9cbfbba04aa81167a889cd9917595ec1686"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
