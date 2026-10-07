class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.90/CSharpFar-v1.0.90-osx-arm64.tar.gz"
      sha256 "316be991101b7bb31f9b78b8ecbae9f9dac9b0e1373305440d966fb32bd8d9a8"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.90/CSharpFar-v1.0.90-osx-x64.tar.gz"
      sha256 "b3aae086e393cfc47eeaf582fd246c699b5299d6b8cb99b5a7fd8b47a2628110"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
