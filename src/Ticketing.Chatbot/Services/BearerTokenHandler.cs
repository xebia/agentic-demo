using System.Net.Http.Headers;

namespace Ticketing.Chatbot.Services;

public class BearerTokenHandler : DelegatingHandler
{
    private readonly UserSessionService _userSession;

    public BearerTokenHandler(UserSessionService userSession)
    {
        _userSession = userSession;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_userSession.IsAuthenticated && !string.IsNullOrEmpty(_userSession.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _userSession.AccessToken);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
