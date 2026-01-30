namespace Ticketing.Auth.Models;

/// <summary>
/// Request body for obtaining a service account JWT token via client credentials flow.
/// </summary>
public class ClientCredentialsRequest
{
    /// <summary>
    /// The client ID of the service account.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// The client secret of the service account.
    /// </summary>
    public required string ClientSecret { get; set; }
}
