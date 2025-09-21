using System.Collections.Generic;

namespace ZycusSync.Domain.Abstractions
{
    public sealed class GroupMembershipFullRow
    {
        public string Action { get; set; } = ""; // Added/Removed
        public string GroupId { get; set; } = "";
        public string GroupName { get; set; } = "";
        public string UserId { get; set; } = "";
        public string UserDisplayName { get; set; } = "";
        public string UserPrincipalName { get; set; } = "";

        public IDictionary<string, string> ToDictionary() => new Dictionary<string, string>
        {
            ["Action"] = Action,
            ["GroupId"] = GroupId,
            ["GroupName"] = GroupName,
            ["UserId"] = UserId,
            ["UserDisplayName"] = UserDisplayName,
            ["UserPrincipalName"] = UserPrincipalName
        };
    }
}
