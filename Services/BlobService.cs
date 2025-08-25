using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace retailMvcDemo.Services
{
    public interface IBlobService
    {
        Task<string> UploadFileAsync(IFormFile file);
        Task<bool> DeleteFileAsync(string urlOrName);
    }

    public class BlobService : IBlobService
    {
        private readonly BlobContainerClient _container;

        public BlobService(IConfiguration cfg)
        {
            // Connection string & container name
            var conn = cfg.GetSection("AzureBlobStorage")["ConnectionString"]
                      ?? cfg.GetConnectionString("AzureStorage");

            var containerName = cfg.GetSection("AzureBlobStorage")["ContainerName"]
                             ?? "product-images";

            _container = new BlobContainerClient(conn, containerName);
            _container.CreateIfNotExists(); // no public flag
        }

        public async Task<string> UploadFileAsync(IFormFile file)
        {
            if (file == null || file.Length <= 0)
                throw new ArgumentException("File is empty.");

            var ext = Path.GetExtension(file.FileName);
            var name = Path.GetFileNameWithoutExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(name)) name = "image";

            var blobName = $"{Sanitize(name)}_{Guid.NewGuid():N}{ext}";
            var blob = _container.GetBlobClient(blobName);

            await blob.UploadAsync(file.OpenReadStream(), new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType }
            });

            return blob.Uri.ToString(); // store direct URL
        }

        public async Task<bool> DeleteFileAsync(string urlOrName)
        {
            if (string.IsNullOrWhiteSpace(urlOrName)) return false;

            var blobName = TryExtractBlobName(urlOrName) ?? urlOrName.Trim();
            var blob = _container.GetBlobClient(blobName);

            var resp = await blob.DeleteIfExistsAsync();
            return resp.Value;
        }

        // --- helpers ---
        private static string Sanitize(string input)
        {
            var cleaned = new string(input.Select(ch =>
                char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'
            ).ToArray());

            return string.IsNullOrWhiteSpace(cleaned) ? "image" : cleaned.ToLowerInvariant();
        }

        private string? TryExtractBlobName(string maybeUrl)
        {
            if (Uri.TryCreate(maybeUrl, UriKind.Absolute, out var uri))
            {
                var segments = uri.Segments;
                if (segments?.Length > 0)
                    return Uri.UnescapeDataString(segments[^1]);
            }
            return null;
        }
    }
}

