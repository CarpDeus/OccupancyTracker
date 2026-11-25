using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OccupancyTracker.DTO;

namespace OccupancyTracker.Service;

public sealed class TrackerApiClient(HttpClient client, TokenProvider tokens)
{
    public Task<EntranceCounterDto?> GetAsync(string counterSqid, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, counterSqid, null, cancellationToken);

    public Task<EntranceCounterDto?> UpdateAsync(string counterSqid, int delta, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, counterSqid, delta, cancellationToken);

    private async Task<EntranceCounterDto?> SendAsync(HttpMethod method, string counterSqid, int? delta, CancellationToken cancellationToken)
    {
        var path = $"api/trackers/{Uri.EscapeDataString(counterSqid)}";
        using var request = new HttpRequestMessage(method, delta.HasValue ? path + "/count" : path);
        if (!string.IsNullOrEmpty(tokens.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        if (delta.HasValue)
            request.Content = JsonContent.Create(new { delta = delta.Value });
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EntranceCounterDto>(cancellationToken)
            ?? throw new InvalidDataException("The tracker API returned an empty response.");
    }
}
