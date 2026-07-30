using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Platform.Application.Common.Interfaces;

namespace Platform.Infrastructure.Files;

public class BlobStorageService : IBlobStorageService
{
    private const string ContainerName = "attachments";

    private readonly BlobContainerClient _container;
    private readonly string _accountName;
    private readonly StorageSharedKeyCredential _sharedKeyCredential;

    public BlobStorageService(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BlobStorage")
            ?? throw new InvalidOperationException("ConnectionStrings:BlobStorage is not configured.");

        var serviceClient = new BlobServiceClient(connectionString);
        _container = serviceClient.GetBlobContainerClient(ContainerName);
        _accountName = serviceClient.AccountName;

        // Parsed straight from the connection string rather than a separate config value -
        // generating a SAS token needs the account key directly, and the connection string
        // already has it; no reason to duplicate it as a second configured secret.
        var accountKey = ParseAccountKey(connectionString);
        _sharedKeyCredential = new StorageSharedKeyCredential(_accountName, accountKey);
    }

    public async Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(blobName);
        await blob.UploadAsync(
            content,
            new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType },
            cancellationToken: cancellationToken);
    }

    public Task<string> GetReadUrlAsync(string blobName, TimeSpan validFor, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(blobName);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = ContainerName,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(validFor),
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasToken = sasBuilder.ToSasQueryParameters(_sharedKeyCredential).ToString();
        return Task.FromResult($"{blob.Uri}?{sasToken}");
    }

    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(blobName);
        await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private static string ParseAccountKey(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx > 0 && part[..idx].Equals("AccountKey", StringComparison.OrdinalIgnoreCase))
                return part[(idx + 1)..];
        }
        throw new InvalidOperationException("AccountKey not found in Blob Storage connection string.");
    }
}
