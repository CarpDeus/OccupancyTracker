using System.Net.Http.Headers;
using System.Net.Http.Json;
using OccupancyTracker.DTO;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

public sealed class OrganizationApiClient(HttpClient client, TokenProvider tokens)
{
    public Task<List<Organization>> GetListAsync(CancellationToken cancellationToken = default) =>
        SendAsync<List<Organization>>(HttpMethod.Get, "api/organizations", null, cancellationToken);

    public Task<Organization> GetAsync(string sqid, CancellationToken cancellationToken = default) =>
        SendAsync<Organization>(HttpMethod.Get, $"api/organizations/{Uri.EscapeDataString(sqid)}", null, cancellationToken);

    public Task<Organization> SaveAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        var edit = new OrganizationEdit
        {
            OrganizationName = organization.OrganizationName, OrganizationDescription = organization.OrganizationDescription,
            OrganizationAddress = organization.OrganizationAddress, PhoneNumber = organization.PhoneNumber
        };
        return organization.OrganizationId == 0
            ? SendAsync<Organization>(HttpMethod.Post, "api/organizations", edit, cancellationToken)
            : SendAsync<Organization>(HttpMethod.Put, $"api/organizations/{Uri.EscapeDataString(organization.OrganizationSqid!)}", edit, cancellationToken);
    }

    public Task<Organization> ChangeStatusAsync(string sqid, int fromStatus, int toStatus, CancellationToken cancellationToken = default) =>
        SendAsync<Organization>(HttpMethod.Post, $"api/organizations/{Uri.EscapeDataString(sqid)}/status",
            new OrganizationStatusChange { FromStatus = fromStatus, ToStatus = toStatus }, cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tokens.AccessToken))
            throw new InvalidOperationException("No access token is available; sign in again.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InvalidDataException("The organization API returned an empty response.");
    }
}
