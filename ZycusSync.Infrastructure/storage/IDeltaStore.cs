using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace ZycusSync.Infrastructure.Storage
{
    public interface IDeltaStore
    {
        Task<string?> ReadAsync(string name, CancellationToken ct);
        Task WriteAsync(string name, string value, CancellationToken ct);
    }
}

