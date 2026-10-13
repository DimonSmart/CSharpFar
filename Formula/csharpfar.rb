class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.95/CSharpFar-v1.0.95-osx-arm64.tar.gz"
      sha256 "ea2e7beaa9f3bd3a5ebf4fd2ceffd1f45c91f96f4702bfb4416b245497a69446"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.95/CSharpFar-v1.0.95-osx-x64.tar.gz"
      sha256 "9f82314ca7c0bd7ed02115692a30dc2122922a9bdc2f81c92b21b63014454394"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
