using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;
using FluentFTP;

using static CSharpFar.Tests.RemoteConnectionDialogTestFixture;

namespace CSharpFar.Tests;

public sealed partial class Spec035FtpProviderTests
{
    [Fact]
    public void FtpsCertificateTrust_FirstAcceptPersistsFingerprint()
    {
        using var certificate = CreateCertificate();
        string? acceptedFingerprint = null;

        bool accepted = FtpFilePanelSource.ValidateCertificateFingerprint(
            TestConnection() with { ExpectedTlsCertificateFingerprint = null },
            certificate,
            acceptCertificate: (_, _) => true,
            acceptedCertificate: (_, fingerprint) => acceptedFingerprint = fingerprint,
            out string? errorMessage);

        Assert.True(accepted);
        Assert.Null(errorMessage);
        Assert.Equal(FtpFilePanelSource.FormatCertificateFingerprint(certificate), acceptedFingerprint);
    }

    [Fact]
    public void FtpsCertificateTrust_MatchingFingerprintReconnects()
    {
        using var certificate = CreateCertificate();
        string fingerprint = FtpFilePanelSource.FormatCertificateFingerprint(certificate);

        bool accepted = FtpFilePanelSource.ValidateCertificateFingerprint(
            TestConnection() with { ExpectedTlsCertificateFingerprint = fingerprint },
            certificate,
            acceptCertificate: (_, _) => false,
            acceptedCertificate: null,
            out string? errorMessage);

        Assert.True(accepted);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void FtpsCertificateTrust_ChangedFingerprintIsRejected()
    {
        using var certificate = CreateCertificate();

        bool accepted = FtpFilePanelSource.ValidateCertificateFingerprint(
            TestConnection() with { ExpectedTlsCertificateFingerprint = "AA:BB" },
            certificate,
            acceptCertificate: (_, _) => true,
            acceptedCertificate: null,
            out string? errorMessage);

        Assert.False(accepted);
        Assert.Contains("fingerprint changed", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

}
