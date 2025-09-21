using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZycusSync.Domain.Abstractions;

namespace ZycusSync.Infrastructure.Sinks
{
    public sealed class NoopZycusSink : IZycusSink
    {
        public Task WriteAsync(string datasetName,
                               IEnumerable<IDictionary<string, string>> rows,
                               CancellationToken ct)
            => Task.CompletedTask;
    }
}
