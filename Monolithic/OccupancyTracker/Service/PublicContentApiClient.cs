using System.Net.Http.Json;

namespace OccupancyTracker.Service;

public sealed class PublicContentApiClient(HttpClient client)
{
    public async Task<string> GetAsync(string page, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync($"api/public-content/{Uri.EscapeDataString(page)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<string>(cancellationToken)
            ?? throw new InvalidDataException("The content API returned an empty response.");
    }
}
