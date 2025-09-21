using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Threading;

namespace ZycusSync.Domain.Abstractions;

public interface IDeltaStore
{
    Task<string?> ReadAsync(string key, CancellationToken ct = default);
    Task WriteAsync(string key, string value, CancellationToken ct = default);
}

