using OccupancyTracker.Models;

namespace OccupancyTracker.IService;

/// <summary>Authorization for an HTTP request authenticated by the server's authentication middleware.</summary>
public interface IRequestAuthorizationService
{
    Task<UserInformation?> GetCurrentUserAsync();
    Task<string> GetOrganizationRoleAsync(string organizationSqid);
    Task<string> GetLocationRoleAsync(string organizationSqid, string locationSqid);
    Task<bool> CanUseCounterAsync(string entranceCounterSqid);
}
