using Microsoft.Extensions.Options;
using Ticketing.Auth.Models;

namespace Ticketing.Auth.Services;

/// <summary>
/// Store for user and service account identities.
/// In production, this would query a real identity provider.
/// </summary>
public class IdentityStore
{
    private readonly List<ServiceAccount> _serviceAccounts;

    public IdentityStore(IOptions<List<ServiceAccount>> serviceAccountsOptions)
    {
        _serviceAccounts = serviceAccountsOptions.Value ?? [];
    }

    /// <summary>
    /// Finds a user by ID or email.
    /// </summary>
    public MockUser? FindUser(string? userId, string? email)
    {
        return MockUser.DemoUsers.All.FirstOrDefault(u =>
            (!string.IsNullOrWhiteSpace(userId) &&
             u.Id.Equals(userId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(email) &&
             u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Validates service account credentials.
    /// </summary>
    public ServiceAccount? ValidateServiceAccount(string clientId, string clientSecret)
    {
        return _serviceAccounts.FirstOrDefault(sa =>
            sa.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase) &&
            sa.ClientSecret == clientSecret);
    }

    /// <summary>
    /// Gets all demo users (for listing available users).
    /// </summary>
    public IReadOnlyList<MockUser> GetAllUsers() => MockUser.DemoUsers.All;
}
