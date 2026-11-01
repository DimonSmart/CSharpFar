class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.110/CSharpFar-v1.0.110-osx-arm64.tar.gz"
      sha256 "35c9f6cfd70d8eda52af5c84e7775b412b7a884ecda9122a916b6cd18eb84bc2"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.110/CSharpFar-v1.0.110-osx-x64.tar.gz"
      sha256 "ccec74de9e294ff805d6ced14266905f71f94098752bd8b9c633ef2c4367260b"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
