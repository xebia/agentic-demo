namespace Ticketing.Web.Services.Auth;

/// <summary>
/// Represents a mock user for the demo authentication system.
/// </summary>
public class MockUser
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string[] Roles { get; init; } = [];

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Predefined demo users for different roles.
    /// </summary>
    public static class DemoUsers
    {
        public static MockUser HelpDeskUser { get; } = new()
        {
            Id = "helpdesk-user-1",
            Email = "sarah.helpdesk@company.com",
            DisplayName = "Sarah Chen (Help Desk)",
            Roles = [UserRoles.HelpDesk]
        };

        public static MockUser Approver { get; } = new()
        {
            Id = "approver-1",
            Email = "mike.manager@company.com",
            DisplayName = "Mike Johnson (Manager)",
            Roles = [UserRoles.Approver]
        };

        public static MockUser HelpDeskAndApprover { get; } = new()
        {
            Id = "admin-1",
            Email = "admin@company.com",
            DisplayName = "Admin User",
            Roles = [UserRoles.HelpDesk, UserRoles.Approver]
        };

        public static MockUser Requestor { get; } = new()
        {
            Id = "requestor-1",
            Email = "john.employee@company.com",
            DisplayName = "John Employee",
            Roles = [UserRoles.User]
        };

        public static IReadOnlyList<MockUser> All { get; } =
        [
            HelpDeskUser,
            Approver,
            HelpDeskAndApprover,
            Requestor
        ];
    }
}

/// <summary>
/// Role constants for the ticketing system.
/// </summary>
public static class UserRoles
{
    public const string HelpDesk = "HelpDesk";
    public const string Approver = "Approver";
    public const string User = "User";
}
