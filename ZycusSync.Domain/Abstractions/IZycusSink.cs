using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZycusSync.Domain.Abstractions
{
    public interface IZycusSink
    {
        Task WriteAsync(string datasetName,
                        IEnumerable<IDictionary<string, string>> rows,
                        CancellationToken ct);
    }
}
