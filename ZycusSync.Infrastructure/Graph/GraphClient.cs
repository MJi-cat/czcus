using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ZycusSync.Domain.Abstractions;
//using ZycusSync.Infrastructure.Config;
using ZycusSync.Infrastructure.Options;

namespace ZycusSync.Infrastructure.Graph;

public sealed class GraphClient : IGraphClient
{
    private readonly HttpClient _http;
    private readonly GraphOptions _opts;
    private string? _token;
    private DateTimeOffset _exp = DateTimeOffset.MinValue;

    public GraphClient(HttpClient http, GraphOptions opts)
    {
        _http = http;
        _opts = opts;
    }

    // -------------- public API --------------

    public async Task<Dictionary<string, string>> ExpandToNestedGroupsAsync(
        IEnumerable<(string Id, string DisplayName)> roots,
        CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in roots)
        {
            ids.Add(r.Id); names[r.Id] = r.DisplayName;
            var url = $"https://graph.microsoft.com/v1.0/groups/{r.Id}/transitiveMembers/microsoft.graph.group?$select=id,displayName";
            while (!ct.IsCancellationRequested)
            {
                var root = await GetAsync(url, ct);
                if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                        if (!string.IsNullOrEmpty(id) && ids.Add(id))
                        {
                            var dn = item.TryGetProperty("displayName", out var dnEl) ? dnEl.GetString() : null;
                            names[id] = dn ?? id;
                        }
                    }
                }
                if (TryNext(root, out var next)) url = next!;
                else break;
            }
        }

        return names;
    }

    public async Task<string> RunUntilDeltaAsync(string url, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var root = await GetAsync(url, ct, preferMinimal: false);
            if (TryNext(root, out var next)) { url = next!; continue; }
            if (TryDelta(root, out var delta)) return delta!;
            throw new InvalidOperationException("No nextLink or deltaLink returned.");
        }
        throw new OperationCanceledException(ct);
    }

    public async Task<(List<GroupMembershipFullRow> rows, string finalDelta, Dictionary<string, string> discovered)>
    CollectGroupMembershipChangesAsync(
        string deltaLink,
        HashSet<string> allowedGroupIds,
        Dictionary<string, string> groupIdToName,
        bool requireUserTypeMember,
        string[] staffEmployeeTypes,
        string defaultManagerUPN,
        CancellationToken ct)
    {
        var rows = new List<GroupMembershipFullRow>();
        var discovered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string url = deltaLink;
        string? finalDelta = null;

        while (!ct.IsCancellationRequested)
        {
            var root = await GetAsync(url, ct, preferMinimal: true);
            if (TryDelta(root, out var d)) finalDelta = d;

            if (root.TryGetProperty("value", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                foreach (var grp in groups.EnumerateArray())
                {
                    if (grp.TryGetProperty("@removed", out _)) continue;

                    var gid = grp.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    if (gid.Length == 0) continue;

                    if (grp.TryGetProperty("displayName", out var dn) && !string.IsNullOrEmpty(dn.GetString()))
                        groupIdToName[gid] = dn.GetString()!;

                    if (!allowedGroupIds.Contains(gid)) continue;
                    if (!grp.TryGetProperty("members@delta", out var mdelta) || mdelta.ValueKind != JsonValueKind.Array)
                        continue;

                    var addedUserIds = new List<string>();
                    var removedUserIds = new List<string>();

                    foreach (var m in mdelta.EnumerateArray())
                    {
                        bool isGroup = m.TryGetProperty("@odata.type", out var odt) &&
                                       odt.GetString()?.EndsWith(".group", StringComparison.OrdinalIgnoreCase) == true;

                        if (isGroup && !m.TryGetProperty("@removed", out _))
                        {
                            var sgId = m.TryGetProperty("id", out var sg) ? sg.GetString() : null;
                            if (!string.IsNullOrEmpty(sgId) && allowedGroupIds.Add(sgId))
                            {
                                var gname = await TryGetGroupNameAsync(sgId!, ct);
                                discovered[sgId!] = gname ?? sgId!;
                            }
                            continue;
                        }

                        if (m.TryGetProperty("@removed", out _))
                        {
                            var rid = m.TryGetProperty("id", out var rmIdEl) ? rmIdEl.GetString() ?? "" : "";
                            if (!string.IsNullOrEmpty(rid)) removedUserIds.Add(rid);
                            continue;
                        }

                        var uid = m.TryGetProperty("id", out var idM) ? idM.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(uid)) addedUserIds.Add(uid);
                    }

                    var groupName = groupIdToName.TryGetValue(gid, out var nm) ? nm : gid;

                    if (addedUserIds.Count > 0)
                    {
                        var users = await FetchUsersFullAsyncBatch(addedUserIds, ct);
                        foreach (var u in users)
                        {
                            if (requireUserTypeMember && !string.Equals(u.UserType, "Member", StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (staffEmployeeTypes.Length > 0 &&
                                !staffEmployeeTypes.Any(t => string.Equals(t, u.EmployeeType ?? "", StringComparison.OrdinalIgnoreCase)))
                                continue;

                            EnsureManagerFallback(u, defaultManagerUPN);

                            rows.Add(GroupRow("Added", gid, groupName, u));
                        }
                    }

                    if (removedUserIds.Count > 0)
                    {
                        var users = await FetchUsersFullAsyncBatch(removedUserIds, ct);
                        var found = new HashSet<string>(users.Select(u => u.UserId), StringComparer.OrdinalIgnoreCase);

                        foreach (var u in users)
                        {
                            if (requireUserTypeMember && !string.Equals(u.UserType, "Member", StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (staffEmployeeTypes.Length > 0 &&
                                !staffEmployeeTypes.Any(t => string.Equals(t, u.EmployeeType ?? "", StringComparison.OrdinalIgnoreCase)))
                                continue;

                            EnsureManagerFallback(u, defaultManagerUPN);

                            rows.Add(GroupRow("Removed", gid, groupName, u));
                        }

                        foreach (var missingId in removedUserIds.Where(id => !found.Contains(id)))
                        {
                            rows.Add(new GroupMembershipFullRow
                            {
                                Action = "Removed",
                                GroupId = gid,
                                GroupName = groupName,
                                UserId = missingId,
                                AccountEnabled = false,
                                ManagerUserPrincipalName = defaultManagerUPN,
                                ManagerMail = defaultManagerUPN
                            });
                        }
                    }
                }
            }

            if (TryNext(root, out var next)) { url = next!; continue; }
            break;
        }

        return (rows, finalDelta ?? deltaLink, discovered);

        static GroupMembershipFullRow GroupRow(string action, string gid, string gname, UserFullRow u)
            => new GroupMembershipFullRow
            {
                Action = action,
                GroupId = gid,
                GroupName = gname,
                UserId = u.UserId,
                UserPrincipalName = u.UserPrincipalName,
                DisplayName = u.DisplayName,
                GivenName = u.GivenName,
                Surname = u.Surname,
                Mail = u.Mail,
                JobTitle = u.JobTitle,
                Department = u.Department,
                CompanyName = u.CompanyName,
                EmployeeId = u.EmployeeId,
                EmployeeType = u.EmployeeType,
                UserType = u.UserType,
                CostCenter = u.CostCenter,
                Division = u.Division,
                OfficeLocation = u.OfficeLocation,
                BusinessPhones = u.BusinessPhones,
                MobilePhone = u.MobilePhone,
                UsageLocation = u.UsageLocation,
                City = u.City,
                State = u.State,
                StreetAddress = u.StreetAddress,
                PostalCode = u.PostalCode,
                Country = u.Country,
                AccountEnabled = u.AccountEnabled,
                Ext1 = u.Ext1,
                Ext2 = u.Ext2,
                Ext3 = u.Ext3,
                Ext4 = u.Ext4,
                Ext5 = u.Ext5,
                Ext6 = u.Ext6,
                Ext7 = u.Ext7,
                Ext8 = u.Ext8,
                Ext9 = u.Ext9,
                Ext10 = u.Ext10,
                Ext11 = u.Ext11,
                Ext12 = u.Ext12,
                Ext13 = u.Ext13,
                Ext14 = u.Ext14,
                Ext15 = u.Ext15,
                ManagerId = u.ManagerId,
                ManagerDisplayName = u.ManagerDisplayName,
                ManagerUserPrincipalName = u.ManagerUserPrincipalName,
                ManagerMail = u.ManagerMail
            };
    }

    public async Task<(List<GroupMembershipFullRow> rows, string finalDelta, Dictionary<string, string> discovered)> CollectGroupMembershipChangesAsync(
        string deltaLink,
        HashSet<string> allowedGroupIds,
        Dictionary<string, string> groupIdToName,
        bool requireUserTypeMember,
        string[] staffEmployeeTypes,
        string defaultManagerUPN,
        CancellationToken ct)
    {
        var changedUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string url = deltaLink;
        string? finalDelta = null;

        while (!ct.IsCancellationRequested)
        {
            var root = await GetAsync(url, ct, preferMinimal: true);
            if (TryDelta(root, out var d)) finalDelta = d;

            if (root.TryGetProperty("value", out var users) && users.ValueKind == JsonValueKind.Array)
            {
                foreach (var u in users.EnumerateArray())
                {
                    if (u.TryGetProperty("@removed", out _))
                    {
                        var uid = u.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                        if (uid != null) changedUserIds.Add(uid);
                        continue;
                    }

                    var id = u.TryGetProperty("id", out var idE) ? idE.GetString() : null;
                    if (string.IsNullOrEmpty(id)) continue;

                    if (await IsUserInWatchedGroupsAsync(id!, allowedGroupIds, ct))
                        changedUserIds.Add(id!);
                }
            }

            if (TryNext(root, out var next)) { url = next!; continue; }
            break;
        }

        if (changedUserIds.Count == 0)
            return (new List<UserFullRow>(), finalDelta ?? deltaLink);

        var full = await FetchUsersFullAsyncBatch(changedUserIds, ct);
        var filtered = new List<UserFullRow>();
        foreach (var r in full)
        {
            if (requireUserTypeMember && !string.Equals(r.UserType, "Member", StringComparison.OrdinalIgnoreCase))
                continue;
            if (staffEmployeeTypes.Length > 0 &&
                !staffEmployeeTypes.Any(t => string.Equals(t, r.EmployeeType ?? "", StringComparison.OrdinalIgnoreCase)))
                continue;

            r.Action = r.AccountEnabled ? "Updated" : "Disabled";
            EnsureManagerFallback(r, defaultManagerUPN);
            filtered.Add(r);
        }

        return (filtered, finalDelta ?? deltaLink);
    }

    // -------------- helpers --------------

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct, bool preferMinimal = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));
        req.Headers.Add("ConsistencyLevel", "eventual");
        if (preferMinimal) req.Headers.Add("Prefer", "return=minimal");

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException(body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static bool TryNext(JsonElement root, out string? next)
    {
        next = root.TryGetProperty("@odata.nextLink", out var n) ? n.GetString() : null;
        return !string.IsNullOrEmpty(next);
    }
    private static bool TryDelta(JsonElement root, out string? delta)
    {
        delta = root.TryGetProperty("@odata.deltaLink", out var d) ? d.GetString() : null;
        return !string.IsNullOrEmpty(delta);
    }

    private async Task<string?> TryGetGroupNameAsync(string groupId, CancellationToken ct)
    {
        var url = $"https://graph.microsoft.com/v1.0/groups/{groupId}?$select=displayName";
        var root = await GetAsync(url, ct);
        return root.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
    }

    private async Task<bool> IsUserInWatchedGroupsAsync(string userId, HashSet<string> allowedGroupIds, CancellationToken ct)
    {
        var url = $"https://graph.microsoft.com/v1.0/users/{userId}/transitiveMemberOf/microsoft.graph.group?$select=id";
        while (!ct.IsCancellationRequested)
        {
            var root = await GetAsync(url, ct);
            if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var gid = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (!string.IsNullOrEmpty(gid) && allowedGroupIds.Contains(gid))
                        return true;
                }
            }
            if (TryNext(root, out var next)) url = next!;
            else break;
        }
        return false;
    }

    private async Task<List<UserFullRow>> FetchUsersFullAsyncBatch(IEnumerable<string> ids, CancellationToken ct)
    {
        var uniqueIds = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var results = new Dictionary<string, UserFullRow>(StringComparer.OrdinalIgnoreCase);
        if (uniqueIds.Length == 0) return new();

        const int BatchSize = 20;
        var select = string.Join(",",
            "id", "displayName", "givenName", "surname", "userPrincipalName", "mail", "jobTitle",
            "department", "companyName", "employeeId", "employeeType", "userType", "employeeOrgData",
            "onPremisesExtensionAttributes", "officeLocation", "businessPhones", "mobilePhone",
            "usageLocation", "city", "state", "streetAddress", "postalCode", "country", "accountEnabled");
        var expand = "manager($select=id,displayName,userPrincipalName,mail)";

        for (int i = 0; i < uniqueIds.Length; i += BatchSize)
        {
            var chunk = uniqueIds.Skip(i).Take(BatchSize).ToArray();
            var requests = new List<object>();
            int rid = 1;
            foreach (var id in chunk)
                requests.Add(new { id = (rid++).ToString(), method = "GET", url = $"/users/{id}?$select={select}&$expand={expand}" });

            var body = System.Text.Json.JsonSerializer.Serialize(new { requests }, new JsonSerializerOptions { WriteIndented = false });
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/$batch");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException(json);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("responses", out var resArr) || resArr.ValueKind != JsonValueKind.Array) continue;

            foreach (var r in resArr.EnumerateArray())
            {
                var status = r.GetProperty("status").GetInt32();
                if (status is >= 200 and < 300)
                {
                    var b = r.GetProperty("body");
                    var row = MapUserJsonToFullRow(b);
                    results[row.UserId] = row;
                }
            }
        }

        return results.Values.ToList();
    }

    private static UserFullRow MapUserJsonToFullRow(JsonElement u)
    {
        string? GetStr(string prop) => u.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        var row = new UserFullRow
        {
            Action = "Updated",
            UserId = GetStr("id") ?? "",
            UserPrincipalName = GetStr("userPrincipalName") ?? "",
            DisplayName = GetStr("displayName") ?? "",
            GivenName = GetStr("givenName") ?? "",
            Surname = GetStr("surname") ?? "",
            Mail = GetStr("mail"),
            JobTitle = GetStr("jobTitle"),
            Department = GetStr("department"),
            CompanyName = GetStr("companyName"),
            EmployeeId = GetStr("employeeId"),
            EmployeeType = GetStr("employeeType"),
            UserType = GetStr("userType"),
            OfficeLocation = GetStr("officeLocation"),
            MobilePhone = GetStr("mobilePhone"),
            UsageLocation = GetStr("usageLocation"),
            City = GetStr("city"),
            State = GetStr("state"),
            StreetAddress = GetStr("streetAddress"),
            PostalCode = GetStr("postalCode"),
            Country = GetStr("country"),
            AccountEnabled = u.TryGetProperty("accountEnabled", out var ae) && (ae.ValueKind == JsonValueKind.True || ae.ValueKind == JsonValueKind.False) && ae.GetBoolean()
        };

        if (u.TryGetProperty("businessPhones", out var phones) && phones.ValueKind == JsonValueKind.Array)
            row.BusinessPhones = string.Join("; ", phones.EnumerateArray().Select(x => x.GetString()).Where(s => !string.IsNullOrEmpty(s)));

        if (u.TryGetProperty("employeeOrgData", out var org) && org.ValueKind == JsonValueKind.Object)
        {
            row.CostCenter = org.TryGetProperty("costCenter", out var cc) ? cc.GetString() : null;
            row.Division = org.TryGetProperty("division", out var dv) ? dv.GetString() : null;
        }

        if (u.TryGetProperty("onPremisesExtensionAttributes", out var ext) && ext.ValueKind == JsonValueKind.Object)
        {
            string? GetExt(int n) => ext.TryGetProperty($"extensionAttribute{n}", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            row.Ext1 = GetExt(1); row.Ext2 = GetExt(2); row.Ext3 = GetExt(3); row.Ext4 = GetExt(4); row.Ext5 = GetExt(5);
            row.Ext6 = GetExt(6); row.Ext7 = GetExt(7); row.Ext8 = GetExt(8); row.Ext9 = GetExt(9); row.Ext10 = GetExt(10);
            row.Ext11 = GetExt(11); row.Ext12 = GetExt(12); row.Ext13 = GetExt(13); row.Ext14 = GetExt(14); row.Ext15 = GetExt(15);
        }

        if (u.TryGetProperty("manager", out var mgr) && mgr.ValueKind == JsonValueKind.Object)
        {
            row.ManagerId = mgr.TryGetProperty("id", out var mid) ? mid.GetString() : null;
            row.ManagerDisplayName = mgr.TryGetProperty("displayName", out var mdn) ? mdn.GetString() : null;
            row.ManagerUserPrincipalName = mgr.TryGetProperty("userPrincipalName", out var mupn) ? mupn.GetString() : null;
            row.ManagerMail = mgr.TryGetProperty("mail", out var mm) ? mm.GetString() : null;
        }

        return row;
    }

    private static void EnsureManagerFallback(UserFullRow row, string defaultUPN)
    {
        if (string.IsNullOrEmpty(row.ManagerUserPrincipalName) && string.IsNullOrEmpty(row.ManagerMail))
        {
            row.ManagerUserPrincipalName = defaultUPN;
            row.ManagerMail = defaultUPN;
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token != null && DateTimeOffset.UtcNow < _exp.AddMinutes(-5))
            return _token;

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _opts.ClientId,
            ["client_secret"] = _opts.ClientSecret,
            ["scope"] = "https://graph.microsoft.com/.default"
        };
        using var content = new FormUrlEncodedContent(form);
        using var resp = await _http.PostAsync($"https://login.microsoftonline.com/{_opts.TenantId}/oauth2/v2.0/token", content, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(json);
        _token = doc.RootElement.GetProperty("access_token").GetString()!;
        _exp = DateTimeOffset.UtcNow.AddMinutes(55);
        return _token!;
    }
}
