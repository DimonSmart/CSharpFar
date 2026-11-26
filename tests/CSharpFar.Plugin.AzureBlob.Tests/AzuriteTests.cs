using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace CSharpFar.Plugin.AzureBlob.Tests;

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CSHARPFAR_AZURE_TEST_CONNECTION")))
            Skip = "Set CSHARPFAR_AZURE_TEST_CONNECTION to an isolated test storage account.";
    }
}

public sealed class AzuriteTests
{
    [IntegrationFact]
    [Trait("Category", "AzureIntegration")]
    public async Task ProviderRoundTripAndConflicts()
    {
        var connection = new AzureBlobConnection(Guid.NewGuid(), "Test", AuthenticationMode.ConnectionString,
            null, null, "/", "CSHARPFAR_AZURE_TEST_CONNECTION", false);
        var (service, _) = AzureBlobClientFactory.Create(connection);
        var container = service!.GetBlobContainerClient("csharpfar-" + Guid.NewGuid().ToString("N"));
        await container.CreateAsync();
        try
        {
            Assert.Contains(container.Name, service.GetBlobContainers().Select(c => c.Name));
            var source = new AzureBlobFilePanelSource(container, "Test", false);
            await source.CreateDirectoryAsync("/empty");
            Assert.True((await source.GetItemAsync("/empty"))!.IsDirectory);
            Assert.Empty(await source.EnumerateDirectoryAsync("/empty"));
            await source.CreateDirectoryAsync("/documents");
            await source.CreateDirectoryAsync("/documents-backup");
            await using (var stream = await source.OpenStagingWriteAsync("/documents/stage"))
                await stream.WriteAsync("hello"u8.ToArray());
            await source.RenameWithoutOverwriteAsync("/documents/stage", "/documents/ñ.txt");
            Assert.Null(await source.GetItemAsync("/documents/stage"));
            await using (var read = await source.OpenReadAsync("/documents/ñ.txt"))
                Assert.Equal(SHA256.HashData("hello"u8), await SHA256.HashDataAsync(read));
            await using (var stream = await source.OpenWriteAsync("/documents/Ñ.txt", false)) { }
            Assert.Equal(0L, (await source.GetItemAsync("/documents/Ñ.txt"))!.Size);
            Assert.Equal(2, (await source.EnumerateDirectoryAsync("/documents")).Count);
            await Assert.ThrowsAnyAsync<IOException>(() => source.OpenStagingWriteAsync("/documents/ñ.txt"));
            await Assert.ThrowsAnyAsync<IOException>(() => source.OpenWriteAsync("/documents/ñ.txt", true));
            await using (var stream = await source.OpenStagingWriteAsync("/documents/conflict")) { }
            await Assert.ThrowsAnyAsync<IOException>(() => source.RenameWithoutOverwriteAsync("/documents/conflict", "/documents/ñ.txt"));
            Assert.NotNull(await source.GetItemAsync("/documents/conflict"));
            await source.DeleteAsync("/documents/conflict", false);
            var readOnly = new AzureBlobFilePanelSource(container, "Read only", true);
            await Assert.ThrowsAnyAsync<IOException>(() => readOnly.DeleteAsync("/documents", true));
            await Assert.ThrowsAnyAsync<IOException>(() => source.DeleteAsync("/", true));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.OpenStagingWriteAsync("/cancelled", cancellation.Token));
            }
            await source.RenameWithoutOverwriteAsync("/documents", "/renamed");
            Assert.Null(await source.GetItemAsync("/documents"));
            Assert.NotNull(await source.GetItemAsync("/renamed/ñ.txt"));
            await source.DeleteAsync("/renamed", true);
            Assert.Null(await source.GetItemAsync("/renamed"));
            Assert.NotNull(await source.GetItemAsync("/documents-backup"));
            await source.DeleteAsync("/empty", false);
            Assert.Null(await source.GetItemAsync("/empty"));
        }
        finally { await container.DeleteIfExistsAsync(); }
    }

    [IntegrationFact]
    [Trait("Category", "AzureIntegration")]
    public async Task LargeStreamingFileAndDirectoryListing()
    {
        var connection = new AzureBlobConnection(Guid.NewGuid(), "Test", AuthenticationMode.ConnectionString,
            null, null, "/", "CSHARPFAR_AZURE_TEST_CONNECTION", false);
        var (service, _) = AzureBlobClientFactory.Create(connection);
        var container = service!.GetBlobContainerClient("csharpfar-" + Guid.NewGuid().ToString("N"));
        await container.CreateAsync();
        try
        {
            var provider = new AzureBlobFilePanelSource(container, "Test", false);
            var buffer = new byte[1024 * 1024];
            new Random(17).NextBytes(buffer);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var write = await provider.OpenStagingWriteAsync("/large-stage"))
            {
                for (int i = 0; i < 100; i++)
                {
                    await write.WriteAsync(buffer);
                    hash.AppendData(buffer);
                }
            }
            await provider.RenameWithoutOverwriteAsync("/large-stage", "/large.bin");
            await using (var read = await provider.OpenReadAsync("/large.bin"))
                Assert.Equal(hash.GetHashAndReset(), await SHA256.HashDataAsync(read));
            Assert.Equal(100L * buffer.Length, (await provider.GetItemAsync("/large.bin"))!.Size);
            await container.GetBlobClient("nested/one/file").UploadAsync(new BinaryData("data"));
            await container.GetBlobClient("nested/two/file").UploadAsync(new BinaryData("data"));
            Assert.Equal(2, (await provider.EnumerateDirectoryAsync("/nested")).Count);
            Assert.All(await provider.EnumerateDirectoryAsync("/nested"), item => Assert.True(item.IsDirectory));
            await container.GetBlobClient("nested/user-zero").UploadAsync(BinaryData.FromBytes(Array.Empty<byte>()));
            Assert.Contains(await provider.EnumerateDirectoryAsync("/nested"), item => item.Name == "user-zero" && !item.IsDirectory);
        }
        finally { await container.DeleteIfExistsAsync(); }
    }
}
