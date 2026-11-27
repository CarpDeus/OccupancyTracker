using System.Net.Http.Headers;
using System.Net.Http.Json;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

public sealed class ProfileApiClient(HttpClient client, TokenProvider tokens)
{
    public Task<UserInformation> GetAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "api/me", null, cancellationToken);

    public Task<UserInformation> SaveAsync(UserInformation profile, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, "api/me", profile, cancellationToken);

    public Task<UserInformation> GetUserAsync(string sqid, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, $"api/admin/users/{Uri.EscapeDataString(sqid)}", null, cancellationToken);

    public Task<UserInformation> SaveUserAsync(string sqid, UserInformation profile, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"api/admin/users/{Uri.EscapeDataString(sqid)}", profile, cancellationToken);

    private async Task<UserInformation> SendAsync(HttpMethod method, string path, UserInformation? profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tokens.AccessToken))
            throw new InvalidOperationException("No access token is available; sign in again.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        if (profile != null) request.Content = JsonContent.Create(profile);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserInformation>(cancellationToken)
            ?? throw new InvalidDataException("The profile API returned an empty response.");
    }
}
