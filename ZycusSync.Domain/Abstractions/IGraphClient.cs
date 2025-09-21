using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZycusSync.Domain.Abstractions
{
    public interface IGraphClient
    {
        // Expand root group IDs → all transitive (groupId → displayName)
        Task<Dictionary<string, string>> ExpandToNestedGroupsAsync(
            IEnumerable<string> rootGroupIds,
            CancellationToken ct);

        // Initial “run-until-delta” for users
        Task<string> RunUntilUsersDeltaAsync(CancellationToken ct);

        // Users profile changes (filtered to allowedGroupIds if you use it that way)
        Task<(List<UserFullRow> rows, string finalDelta)> CollectUserProfileChangesAsync(
            string deltaLink,
            HashSet<string> allowedGroupIds,
            string defaultManagerUPN,
            bool preferMinimal,
            string[] trackedProps,
            CancellationToken ct);

        // Group membership changes (adds/removes)
        Task<(List<GroupMembershipFullRow> rows, string finalDelta, Dictionary<string, string> discovered)> CollectGroupMembershipChangesAsync(
            string deltaLink,
            HashSet<string> allowedGroupIds,
            Dictionary<string, string> groupIdToName,
            bool requireUserTypeMember,
            string[] staffEmployeeTypes,
            string defaultManagerUPN,
            CancellationToken ct);
    }
}
