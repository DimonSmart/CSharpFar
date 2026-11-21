class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.122/CSharpFar-v1.0.122-osx-arm64.tar.gz"
      sha256 "568f0e52d359f1c2bce607f6377f4caf174b2368511e9860d5678827a74c5f8c"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.122/CSharpFar-v1.0.122-osx-x64.tar.gz"
      sha256 "dde6c7058b57b56aaaf8778f0debe433018470be4b551897331039bfbbddc900"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
