using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZycusSync.Infrastructure.Storage
{
    public sealed class FileDeltaStore : IDeltaStore
    {
        private readonly string _root;
        public FileDeltaStore(string root) => _root = root;

        public async Task<string?> ReadAsync(string name, CancellationToken ct)
        {
            var path = Path.Combine(_root, name);
            if (!File.Exists(path)) return null;
            return await File.ReadAllTextAsync(path, ct);
        }

        public async Task WriteAsync(string name, string value, CancellationToken ct)
        {
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, name);
            await File.WriteAllTextAsync(path, value, ct);
        }
    }
}

