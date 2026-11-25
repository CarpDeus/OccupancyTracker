using System.Net.Http.Headers;
using System.Net.Http.Json;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

public sealed class OrganizationApiClient(HttpClient client, TokenProvider tokens)
{
    public async Task<List<Organization>> GetListAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tokens.AccessToken))
            throw new InvalidOperationException("No access token is available; sign in again.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/organizations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Organization>>(cancellationToken)
            ?? throw new InvalidDataException("The organization API returned an empty response.");
    }
}
