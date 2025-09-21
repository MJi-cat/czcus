using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZycusSync.Domain.Users;

public sealed record GroupInfo(string Id, string DisplayName);

public sealed class UserChange
{
    public string UserId { get; init; } = "";
    public string? DisplayName { get; init; }
    public string? GivenName { get; init; }
    public string? Surname { get; init; }
    public string? UserPrincipalName { get; init; }
    public string? Mail { get; init; }
    public string[] ChangedProps { get; init; } = Array.Empty<string>();
}
