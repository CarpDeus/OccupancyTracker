using System.Net;
using System.Net.Http.Headers;

namespace OccupancyTracker.Service;

public sealed class AdminApiClient(HttpClient client, TokenProvider tokens)
{
    public async Task RequestCacheFlushAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tokens.AccessToken))
            throw new InvalidOperationException("No access token is available; sign in again.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/admin/cache/flush");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, cancellationToken); }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("The administration API request timed out.", ex);
        }
        using (response)
        {
            response.EnsureSuccessStatusCode();
            if (response.StatusCode != HttpStatusCode.Accepted)
                throw new HttpRequestException("The administration API returned an unexpected response.", null, response.StatusCode);
        }
    }
}
