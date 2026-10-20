class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.100/CSharpFar-v1.0.100-osx-arm64.tar.gz"
      sha256 "e053251c5e88b79dd4b41676526ee605eb51759284b9c0815ebf0a63e4fdf078"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.100/CSharpFar-v1.0.100-osx-x64.tar.gz"
      sha256 "0f64f44e4673b2c6b683153995a165c787d3815e69982b15f76fc711f98d992e"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
