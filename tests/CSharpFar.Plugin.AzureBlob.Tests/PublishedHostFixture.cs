using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace CSharpFar.Plugin.AzureBlob.Tests;

public sealed class HostFixtureFactAttribute : FactAttribute
{
    public HostFixtureFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CSHARPFAR_AZURE_HOST_FIXTURE") != "1")
            Skip = "Only run against the disposable CI Azurite service.";
    }
}

public sealed class PublishedHostFixture
{
    [HostFixtureFact]
    [Trait("Category", "AzureHostFixture")]
    public async Task SeedDisposableHostFixture()
    {
        var profile = new AzureBlobConnection(Guid.NewGuid(), "AzureSmoke", AuthenticationMode.Azurite,
            null, "csharpfar-smoke", "/", null, false);
        var (_, container) = AzureBlobClientFactory.Create(profile);
        await container!.CreateAsync();
        await container.GetBlobClient("folder/nested.txt").UploadAsync(BinaryData.FromString("NESTED_AZURE_PROBE"));
        await container.GetBlobClient("probe-view.txt").UploadAsync(BinaryData.FromString("AZURE_PROBE_VIEW"));
    }
}
