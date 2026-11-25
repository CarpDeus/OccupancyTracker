using System.Net.Http.Headers;
using System.Net.Http.Json;
using OccupancyTracker.DTO;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

public sealed class FacilityApiClient(HttpClient client, TokenProvider tokens)
{
    private static string Locations(string org) => $"api/organizations/{Uri.EscapeDataString(org)}/locations";
    private static string Location(string org, string location) => $"{Locations(org)}/{Uri.EscapeDataString(location)}";
    private static string Entrances(string org, string location) => $"{Location(org, location)}/entrances";
    private static string Entrance(string org, string location, string entrance) => $"{Entrances(org, location)}/{Uri.EscapeDataString(entrance)}";

    public Task<List<Location>> ListLocationsAsync(string org, CancellationToken ct = default) =>
        SendAsync<List<Location>>(HttpMethod.Get, Locations(org), null, ct);
    public Task<Location> GetLocationAsync(string org, string location, CancellationToken ct = default) =>
        SendAsync<Location>(HttpMethod.Get, Location(org, location), null, ct);
    public Task<bool> CanCreateLocationAsync(string org, CancellationToken ct = default) =>
        SendAsync<bool>(HttpMethod.Get, $"{Locations(org)}/can-create", null, ct);
    public Task<bool> CanManageLocationAsync(string org, string location, CancellationToken ct = default) =>
        SendAsync<bool>(HttpMethod.Get, $"{Location(org, location)}/can-manage", null, ct);
    public Task<Location> SaveLocationAsync(string org, Location location, CancellationToken ct = default)
    {
        var edit = new LocationEdit
        {
            LocationName = location.LocationName, LocationDescription = location.LocationDescription,
            LocationAddress = location.LocationAddress, PhoneNumber = location.PhoneNumber,
            MaxOccupancy = location.MaxOccupancy, OccupancyThresholdWarning = location.OccupancyThresholdWarning
        };
        return location.LocationId == 0
            ? SendAsync<Location>(HttpMethod.Post, Locations(org), edit, ct)
            : SendAsync<Location>(HttpMethod.Put, Location(org, location.LocationSqid!), edit, ct);
    }
    public Task<Location> ChangeLocationStatusAsync(string org, string location, int from, int to, CancellationToken ct = default) =>
        SendAsync<Location>(HttpMethod.Post, $"{Location(org, location)}/status", new OrganizationStatusChange { FromStatus = from, ToStatus = to }, ct);
    public Task<List<Entrance>> ListEntrancesAsync(string org, string location, CancellationToken ct = default) =>
        SendAsync<List<Entrance>>(HttpMethod.Get, Entrances(org, location), null, ct);
    public Task<Entrance> GetEntranceAsync(string org, string location, string entrance, CancellationToken ct = default) =>
        SendAsync<Entrance>(HttpMethod.Get, Entrance(org, location, entrance), null, ct);
    public Task<Entrance> SaveEntranceAsync(string org, string location, Entrance entrance, CancellationToken ct = default)
    {
        var edit = new EntranceEdit { EntranceName = entrance.EntranceName, EntranceDescription = entrance.EntranceDescription };
        return entrance.EntranceId == 0
            ? SendAsync<Entrance>(HttpMethod.Post, Entrances(org, location), edit, ct)
            : SendAsync<Entrance>(HttpMethod.Put, Entrance(org, location, entrance.EntranceSqid), edit, ct);
    }
    public Task<Entrance> ChangeEntranceStatusAsync(string org, string location, string entrance, int from, int to, CancellationToken ct = default) =>
        SendAsync<Entrance>(HttpMethod.Post, $"{Entrance(org, location, entrance)}/status", new OrganizationStatusChange { FromStatus = from, ToStatus = to }, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(tokens.AccessToken))
            throw new InvalidOperationException("No access token is available; sign in again.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        if (body != null) request.Content = JsonContent.Create(body);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException("The facility API request timed out.", ex);
        }
        using var responseScope = response;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct)
            ?? throw new InvalidDataException("The facility API returned an empty response.");
    }
}
