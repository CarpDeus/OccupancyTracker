using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

/// <summary>Evaluates roles that have already been loaded for a single user.</summary>
public static class ResourceAuthorization
{
    public static string OrganizationRole(
        IEnumerable<CurrentUserRoleInformation> roles, string organizationSqid, bool isSuperAdmin)
    {
        if (string.IsNullOrEmpty(organizationSqid))
            return string.Empty;
        if (isSuperAdmin)
            return AuthorizationRecords.Roles.SuperAdmin.Name;

        var matching = roles.Where(r => r.OrganizationSqid == organizationSqid).ToList();
        if (matching.Any(r => r.OrganizationWide && r.RoleName == AuthorizationRecords.Roles.OrganizationAdmin.Name))
            return AuthorizationRecords.Roles.OrganizationAdmin.Name;
        if (matching.Any(r => r.OrganizationWide && r.RoleName == AuthorizationRecords.Roles.LocationAdmin.Name))
            return AuthorizationRecords.Roles.LocationAdmin.Name;
        return matching.Any(IsRecognizedRole) ? AuthorizationRecords.Roles.User.Name : string.Empty;
    }

    public static string LocationRole(
        IEnumerable<CurrentUserRoleInformation> roles, string organizationSqid, string locationSqid, bool isSuperAdmin)
    {
        if (string.IsNullOrEmpty(organizationSqid) || string.IsNullOrEmpty(locationSqid))
            return string.Empty;
        if (isSuperAdmin)
            return AuthorizationRecords.Roles.SuperAdmin.Name;

        var matching = roles.Where(r => r.OrganizationSqid == organizationSqid &&
            (r.OrganizationWide || r.LocationSqid == locationSqid)).ToList();
        if (matching.Any(r => r.OrganizationWide && r.RoleName == AuthorizationRecords.Roles.OrganizationAdmin.Name))
            return AuthorizationRecords.Roles.OrganizationAdmin.Name;
        if (matching.Any(r => r.RoleName == AuthorizationRecords.Roles.LocationAdmin.Name))
            return AuthorizationRecords.Roles.LocationAdmin.Name;
        return matching.Any(IsRecognizedRole) ? AuthorizationRecords.Roles.User.Name : string.Empty;
    }

    private static bool IsRecognizedRole(CurrentUserRoleInformation role) =>
        role.RoleName == AuthorizationRecords.Roles.User.Name ||
        role.RoleName == AuthorizationRecords.Roles.LocationAdmin.Name ||
        (role.OrganizationWide && role.RoleName == AuthorizationRecords.Roles.OrganizationAdmin.Name);
}
