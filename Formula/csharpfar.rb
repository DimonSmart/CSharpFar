class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.89/CSharpFar-v1.0.89-osx-arm64.tar.gz"
      sha256 "7dbf808442f818c3e2181d16aa672b4b3d8737bf8b920b76bb58f5428f9db10c"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.89/CSharpFar-v1.0.89-osx-x64.tar.gz"
      sha256 "fb679a7dea65451e22cc80bebf1fe1851fcede68a38de3219e24a84daafe67b7"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
