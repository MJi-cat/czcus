using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;

namespace ZycusSync.Infrastructure.Storage
{
    public sealed class BlobDeltaStore : IDeltaStore
    {
        private readonly BlobContainerClient _container;
        private readonly string _prefix;

        public BlobDeltaStore(string connectionString, string container, string prefix)
        {
            _container = new BlobContainerClient(connectionString, container);
            _container.CreateIfNotExists();
            _prefix = string.IsNullOrWhiteSpace(prefix) ? "" : prefix.TrimEnd('/') + "/";
        }

        public async Task<string?> ReadAsync(string name, CancellationToken ct)
        {
            var blob = _container.GetBlobClient(_prefix + name);
            if (!await blob.ExistsAsync(ct)) return null;
            using var ms = new MemoryStream();
            await blob.DownloadToAsync(ms, ct);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        public async Task WriteAsync(string name, string value, CancellationToken ct)
        {
            var blob = _container.GetBlobClient(_prefix + name);
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(value));
            await blob.UploadAsync(ms, overwrite: true, cancellationToken: ct);
        }
    }
}
