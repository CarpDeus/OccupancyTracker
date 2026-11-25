using System.Net.Http.Headers;
using System.Net.Http.Json;
using OccupancyTracker.DTO;

namespace OccupancyTracker.Service;

public sealed class OrganizationUserApiClient(HttpClient client, TokenProvider tokens)
{
    private static string Organization(string sqid) => $"api/organizations/{Uri.EscapeDataString(sqid)}";

    public Task<bool> CanManageAsync(string org, CancellationToken ct = default) =>
        SendAsync<bool>(HttpMethod.Get, $"{Organization(org)}/users/can-manage", null, ct);
    public Task<List<OrganizationUserSummary>> GetUsersAsync(string org, CancellationToken ct = default) =>
        SendAsync<List<OrganizationUserSummary>>(HttpMethod.Get, $"{Organization(org)}/users", null, ct);
    public Task<OrganizationMembership> GetMembershipAsync(string org, string user, CancellationToken ct = default) =>
        SendAsync<OrganizationMembership>(HttpMethod.Get, $"{Organization(org)}/users/{Uri.EscapeDataString(user)}", null, ct);
    public Task<OrganizationMembership> SaveMembershipAsync(string org, string user, MembershipEdit edit, CancellationToken ct = default) =>
        SendAsync<OrganizationMembership>(HttpMethod.Put, $"{Organization(org)}/users/{Uri.EscapeDataString(user)}", edit, ct);
    public Task<List<OrganizationInvitationSummary>> GetInvitationsAsync(string org, CancellationToken ct = default) =>
        SendAsync<List<OrganizationInvitationSummary>>(HttpMethod.Get, $"{Organization(org)}/invitations", null, ct);
    public Task<bool> InviteAsync(string org, string email, CancellationToken ct = default) =>
        SendAsync<bool>(HttpMethod.Post, $"{Organization(org)}/invitations", new InviteOrganizationUser { EmailAddress = email }, ct);
    public Task<bool> RedeemAsync(string code, CancellationToken ct = default) =>
        SendAsync<bool>(HttpMethod.Post, "api/me/invitations/redeem", new RedeemOrganizationInvitation { InvitationCode = code }, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(tokens.AccessToken))
            throw new InvalidOperationException("No access token is available; sign in again.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        if (body != null) request.Content = JsonContent.Create(body);
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, ct); }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException("The user management API request timed out.", ex);
        }
        using var responseScope = response;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct)
            ?? throw new InvalidDataException("The user management API returned an empty response.");
    }
}
