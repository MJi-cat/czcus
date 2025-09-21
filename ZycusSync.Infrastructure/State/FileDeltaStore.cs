using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZycusSync.Domain.Abstractions;

namespace ZycusSync.Infrastructure.State;

public sealed class FileDeltaStore : IDeltaStore
{
    private readonly string _root;
    public FileDeltaStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public async Task<string?> ReadAsync(string key, CancellationToken ct = default)
    {
        var path = Path.Combine(_root, key.Replace('/', '_'));
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path, ct);
    }

    public async Task WriteAsync(string key, string value, CancellationToken ct = default)
    {
        var path = Path.Combine(_root, key.Replace('/', '_'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, value, ct);
    }
}
