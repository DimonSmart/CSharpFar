class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.106/CSharpFar-v1.0.106-osx-arm64.tar.gz"
      sha256 "615932f54655e4161826eb178dbd0bb3768f2e78e4911c5310165f5ef395ec4f"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.106/CSharpFar-v1.0.106-osx-x64.tar.gz"
      sha256 "129c5ffb87e49a79f812c66987747b975c79e9f5ae237ee3d954d3ef57437f48"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
