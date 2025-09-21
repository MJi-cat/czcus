using System.Collections.Generic;

namespace ZycusSync.Domain.Abstractions
{
    public sealed class UserFullRow
    {
        public string Action { get; set; } = "Updated";
        public string UserId { get; set; } = "";
        public string UserPrincipalName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string GivenName { get; set; } = "";
        public string Surname { get; set; } = "";
        public string Mail { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string Department { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string EmployeeType { get; set; } = "";
        public string UserType { get; set; } = "";
        public string? CostCenter { get; set; }
        public string? Division { get; set; }
        public string OfficeLocation { get; set; } = "";
        public string BusinessPhones { get; set; } = "";
        public string MobilePhone { get; set; } = "";
        public string? UsageLocation { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? StreetAddress { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public bool AccountEnabled { get; set; }

        // manager
        public string? ManagerId { get; set; }
        public string? ManagerDisplayName { get; set; }
        public string? ManagerUserPrincipalName { get; set; }
        public string? ManagerMail { get; set; }

        public IDictionary<string, string> ToDictionary() => new Dictionary<string, string>
        {
            ["Action"] = Action,
            ["UserId"] = UserId,
            ["UserPrincipalName"] = UserPrincipalName,
            ["DisplayName"] = DisplayName,
            ["GivenName"] = GivenName,
            ["Surname"] = Surname,
            ["Mail"] = Mail,
            ["JobTitle"] = JobTitle,
            ["Department"] = Department,
            ["CompanyName"] = CompanyName,
            ["EmployeeId"] = EmployeeId,
            ["EmployeeType"] = EmployeeType,
            ["UserType"] = UserType,
            ["CostCenter"] = CostCenter ?? "",
            ["Division"] = Division ?? "",
            ["OfficeLocation"] = OfficeLocation,
            ["BusinessPhones"] = BusinessPhones,
            ["MobilePhone"] = MobilePhone,
            ["UsageLocation"] = UsageLocation ?? "",
            ["City"] = City ?? "",
            ["State"] = State ?? "",
            ["StreetAddress"] = StreetAddress ?? "",
            ["PostalCode"] = PostalCode ?? "",
            ["Country"] = Country ?? "",
            ["AccountEnabled"] = AccountEnabled.ToString(),
            ["ManagerId"] = ManagerId ?? "",
            ["ManagerDisplayName"] = ManagerDisplayName ?? "",
            ["ManagerUserPrincipalName"] = ManagerUserPrincipalName ?? "",
            ["ManagerMail"] = ManagerMail ?? ""
        };
    }
}
