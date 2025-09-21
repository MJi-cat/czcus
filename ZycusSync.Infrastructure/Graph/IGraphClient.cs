using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZycusSync.Domain.Abstractions;

namespace ZycusSync.Infrastructure.Graph
{
    public interface IGraphClient
    {
        Task<string> RunUntilDeltaAsync(string url, CancellationToken ct);

        Task<(List<GroupMembershipFullRow> rows, string finalDelta, Dictionary<string, string> discovered)>
            CollectGroupMembershipChangesAsync(
                string deltaLink,
                HashSet<string> allowedGroupIds,
                Dictionary<string, string> groupIdToName,
                bool requireUserTypeMember,
                string[] staffEmployeeTypes,
                string defaultManagerUPN,
                CancellationToken ct);

        Task<(List<UserFullRow> rows, string finalDelta)>
            CollectUserProfileChangesAsync(
                string deltaLink,
                HashSet<string> allowedUserIds,
                string defaultManagerUPN,
                bool includeDisabled,
                string[] staffEmployeeTypes,
                CancellationToken ct);

        Task<IReadOnlyList<(string Id, string DisplayName)>> ExpandToNestedGroupsAsync(
            IEnumerable<string> rootGroupIds,
            CancellationToken ct);
    }
}

