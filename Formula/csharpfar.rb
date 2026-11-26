class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.127/CSharpFar-v1.0.127-osx-arm64.tar.gz"
      sha256 "e89bedce396300c0c671a11af90f69031958c7397d4e433bab388469acd1789f"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.127/CSharpFar-v1.0.127-osx-x64.tar.gz"
      sha256 "a5a8a1129783b3a2958c1c2fc75c20910e4be4714e26808984a3b2855aa40f21"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
