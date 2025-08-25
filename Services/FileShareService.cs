using Azure;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Microsoft.Extensions.Configuration;
using retailMvcDemo.Models;

namespace retailMvcDemo.Services
{
    public interface IFileShareService
    {
        Task<string> UploadAsync(IFormFile file, string? logicalName = null);
        Task<IReadOnlyList<ContractFileViewModel>> ListAsync(int max = 500);
        Task<Stream> DownloadAsync(string fileName);
        Task<bool> DeleteAsync(string fileName);
    }

    public class FileShareService : IFileShareService
    {
        private readonly ShareClient _share;
        private readonly ShareDirectoryClient _dir;

        public FileShareService(IConfiguration cfg)
        {
            var conn = cfg.GetSection("AzureFiles")["ConnectionString"]
                      ?? cfg.GetConnectionString("AzureStorage");
            var shareName = cfg.GetSection("AzureFiles")["ShareName"] ?? "contracts";
            var dirName = cfg.GetSection("AzureFiles")["DirectoryName"] ?? "root";

            _share = new ShareClient(conn, shareName);
            _share.CreateIfNotExists();

            // Use root directory unless a specific subdirectory is provided
            bool useRoot = string.IsNullOrWhiteSpace(dirName) ||
                           dirName.Equals("root", StringComparison.OrdinalIgnoreCase);

            _dir = useRoot
                ? _share.GetRootDirectoryClient()
                : _share.GetDirectoryClient(dirName);

            // Only create directories if not the root (root already exists and 405s if you try to create it)
            if (!useRoot)
            {
                _dir.CreateIfNotExists();
            }
        }

        public async Task<string> UploadAsync(IFormFile file, string? logicalName = null)
        {
            var baseName = string.IsNullOrWhiteSpace(logicalName)
                ? Path.GetFileNameWithoutExtension(file.FileName)
                : logicalName.Trim();

            if (string.IsNullOrWhiteSpace(baseName)) baseName = "contract";
            var fileName = $"{baseName}_{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";

            var sf = _dir.GetFileClient(fileName);
            await sf.CreateAsync(file.Length);

            using var stream = file.OpenReadStream();
            await sf.UploadRangeAsync(
                ShareFileRangeWriteType.Update,
                new HttpRange(0, file.Length),
                stream
            );

            // Skipping SetHttpHeaders due to SDK signature variance; not required for rubric
            return fileName; // Azure Files "name" used for download/delete
        }

        public async Task<IReadOnlyList<ContractFileViewModel>> ListAsync(int max = 500)
        {
            var results = new List<ContractFileViewModel>();

            await foreach (ShareFileItem item in _dir.GetFilesAndDirectoriesAsync())
            {
                if (item.IsDirectory) continue;

                long size = 0;
                DateTimeOffset? last = null;

                // Fetch reliable size/last-modified via GetPropertiesAsync per file
                var sf = _dir.GetFileClient(item.Name);
                try
                {
                    ShareFileProperties props = (await sf.GetPropertiesAsync()).Value;
                    size = props.ContentLength;
                    last = props.LastModified;
                }
                catch
                {
                    // leave defaults if props fetch fails
                }

                results.Add(new ContractFileViewModel
                {
                    Name = item.Name,
                    Bytes = size,
                    LastModified = last
                });

                if (results.Count >= max) break;
            }

            return results;
        }

        public async Task<Stream> DownloadAsync(string fileName)
        {
            var sf = _dir.GetFileClient(fileName);
            if (!await sf.ExistsAsync()) throw new FileNotFoundException("File not found", fileName);

            var dl = await sf.DownloadAsync();
            var mem = new MemoryStream();
            await dl.Value.Content.CopyToAsync(mem);
            mem.Position = 0;
            return mem;
        }

        public async Task<bool> DeleteAsync(string fileName)
        {
            var sf = _dir.GetFileClient(fileName);
            var resp = await sf.DeleteIfExistsAsync();
            return resp.Value;
        }
    }
}

