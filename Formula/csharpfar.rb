class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.86/CSharpFar-v1.0.86-osx-arm64.tar.gz"
      sha256 "9cfa34769d307ddcfd60bedc4aaf9e52cd3a39d5ea1f9913413ac16da057a973"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.86/CSharpFar-v1.0.86-osx-x64.tar.gz"
      sha256 "4de73ae133b644f91351e19084e1967b951a7e0dfd129d498fe13901901a8623"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
