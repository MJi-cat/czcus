using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using ZycusSync.Domain.Abstractions;

namespace ZycusSync.Application.Users;

public sealed class ProcessUserDeltaHandler : IRequestHandler<ProcessUserDelta, Unit>
{
    private readonly ILogger<ProcessUserDeltaHandler> _log;
    private readonly IGraphClient _graph;
    private readonly IDeltaStore _delta;
    private readonly IZycusSink _sink;

    public ProcessUserDeltaHandler(
        ILogger<ProcessUserDeltaHandler> log,
        IGraphClient graph,
        IDeltaStore delta,
        IZycusSink sink)
    {
        _log = log;
        _graph = graph;
        _delta = delta;
        _sink = sink;
    }

    public async Task<Unit> Handle(ProcessUserDelta request, CancellationToken ct)
    {
        _log.LogInformation("Tick at: {ts}", DateTimeOffset.UtcNow);

        // 1) baseline users delta if missing
        var usersToken = await _delta.ReadAsync("users.delta", ct);
        if (string.IsNullOrWhiteSpace(usersToken))
        {
            _log.LogInformation("No users.delta found. Creating baseline...");
            var delta = await _graph.RunUntilDeltaAsync("https://graph.microsoft.com/v1.0/users/delta?$select=displayName,jobTitle,mobilePhone,surname,mail,userPrincipalName", ct);
            await _delta.WriteAsync("users.delta", delta, ct);
            _log.LogInformation("Baseline saved. Next run will emit changes.");
            return Unit.Value;
        }

        // 2) watched groups set (ensure exists)
        var watchedJson = await _delta.ReadAsync("watched-groups.json", ct);
        var watched = string.IsNullOrEmpty(watchedJson)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : JsonSerializer.Deserialize<Dictionary<string, string>>(watchedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var allowedIds = new HashSet<string>(watched.Keys, StringComparer.OrdinalIgnoreCase);

        // 3) collect user profile changes filtered by watched groups
        var (rows, nextUsersDelta) = await _graph.CollectUserProfileChangesAsync(
            usersToken, allowedIds, defaultManagerUPN: "CEWA.admin@zycus.com",
            requireUserTypeMember: true, staffEmployeeTypes: Array.Empty<string>(), ct);

        if (rows.Count == 0)
        {
            _log.LogInformation("No user changes this cycle.");
            return Unit.Value;
        }

        await _sink.WriteAsync("users", rows.Select(r => r.ToDictionary()), ct);
        await _delta.WriteAsync("users.delta", nextUsersDelta, ct);
        _log.LogInformation("Emitted {count} user rows.", rows.Count);
        return Unit.Value;
    }
}
