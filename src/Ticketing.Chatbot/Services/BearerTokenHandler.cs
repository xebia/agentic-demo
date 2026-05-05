using System.Net.Http.Headers;

namespace Ticketing.Chatbot.Services;

public class BearerTokenHandler : DelegatingHandler
{
    private readonly UserSessionService _userSession;
    private readonly ILogger<BearerTokenHandler>? _logger;

    public BearerTokenHandler(UserSessionService userSession, ILogger<BearerTokenHandler>? logger = null)
    {
        _userSession = userSession;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var hasToken = _userSession.IsAuthenticated && !string.IsNullOrEmpty(_userSession.AccessToken);
        if (hasToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _userSession.AccessToken);
        }
        _logger?.LogInformation(
            "MCP HTTP {Method} {Url} — bearer attached: {HasToken}",
            request.Method, request.RequestUri, hasToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
