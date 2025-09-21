using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using ZycusSync.Domain.Abstractions;
using ZycusSync.Infrastructure.Graph;
using ZycusSync.Infrastructure.Storage;

namespace ZycusSync.Application.Groups
{
    public sealed class ProcessGroupDeltaHandler : IRequestHandler<ProcessGroupDelta, Unit>
    {
        private readonly ILogger<ProcessGroupDeltaHandler> _log;
        private readonly IGraphClient _graph;
        private readonly IDeltaStore _delta;
        private readonly IZycusSink _sink;

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        // Change these to your root group ids (or load from file/config as you had)
        private static readonly string[] RootGroupIds =
        {
            "09a89ccd-0517-406c-a24e-c36ebe611a3e" // SG-8445-Zycus-Access-Test
        };

        public ProcessGroupDeltaHandler(
            ILogger<ProcessGroupDeltaHandler> log,
            IGraphClient graph,
            IDeltaStore delta,
            IZycusSink sink)
        {
            _log = log;
            _graph = graph;
            _delta = delta;
            _sink = sink;
        }

        public async Task<Unit> Handle(ProcessGroupDelta request, CancellationToken ct)
        {
            // 1) expand roots → nested groups
            var nested = await _graph.ExpandToNestedGroupsAsync(RootGroupIds, ct);
            var watchedIds = new HashSet<string>(nested.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
            var idToName = nested.ToDictionary(n => n.Id, n => n.DisplayName ?? n.Id, StringComparer.OrdinalIgnoreCase);

            _log.LogInformation("Loaded watched group set: {count} groups.", watchedIds.Count);

            // 2) ensure baseline for groups
            var groupsDelta = await _delta.ReadAsync("groups.delta", ct);
            if (string.IsNullOrWhiteSpace(groupsDelta))
            {
                _log.LogInformation("No groups.delta found. Creating baseline...");
                var initUrl = "https://graph.microsoft.com/v1.0/groups/delta?$select=id,displayName,members";
                var deltaLink = await _graph.RunUntilDeltaAsync(initUrl, ct);
                await _delta.WriteAsync("groups.delta", deltaLink, ct);
                _log.LogInformation("Baseline saved. Next run will emit membership changes.");
                return Unit.Value;
            }

            // 3) collect adds/removes for watched groups
            var (rows, finalDelta, discovered) = await _graph.CollectGroupMembershipChangesAsync(
                groupsDelta,
                watchedIds,
                idToName,
                requireUserTypeMember: true,
                staffEmployeeTypes: Array.Empty<string>(),
                defaultManagerUPN: "CEWA.admin@zycus.com",
                ct);

            if (rows.Count == 0)
            {
                _log.LogInformation("No group membership changes this cycle.");
            }
            else
            {
                await _sink.WriteAsync("group_membership_changes",
                    rows.Select(r => r.ToDictionary()), ct);

                var json = JsonSerializer.Serialize<object>(rows, JsonOpts);
                var path = Path.Combine(AppContext.BaseDirectory, $"group_membership_changes_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                await File.WriteAllTextAsync(path, json, ct);
                _log.LogInformation("Wrote {count} group membership rows → {path}", rows.Count, path);
            }

            // 4) persist delta
            await _delta.WriteAsync("groups.delta", finalDelta, ct);

            // 5) if new subgroups discovered at runtime, you can persist them to your watched file here (optional)

            return Unit.Value;
        }
    }
}
