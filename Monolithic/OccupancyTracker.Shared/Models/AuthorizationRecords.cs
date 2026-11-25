namespace OccupancyTracker.Models
{
    public class AuthorizationRecords
    {
        public record UserStatus(int Id, string Name, string Description)
        {
            public static UserStatus Approved { get; } = new UserStatus(0, "Approved", "User has access to the organization");
            public static UserStatus NoAccess { get; } = new UserStatus(1, "NoAccess", "User is denied access to the organization");
            public static UserStatus Deleted { get; } = new UserStatus(2, "PermanentlyDeleted", "User has deleted their information");
            public static UserStatus Invited { get; } = new UserStatus(2, "Invited", "User has been invited but has not completed registration for the organization");

            public static UserStatus FromId(int id)
            {
                return id switch
                {
                    0 => Approved,
                    1 => NoAccess,
                    2 => Deleted,
                    3 => Invited,
                    _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Invalid status id")
                };
            }
        }

        public record Roles(int Id, string Name, string Description)
        {
            public static Roles NoAccess { get; } = new Roles(-1, "No Access", "User denied access");
            public static Roles User { get; } = new Roles(0, "User", "User with access");
            public static Roles LocationAdmin { get; } = new Roles(1, "LocationAdmin", "User able to modify locations for an organization and access to the location");
            public static Roles OrganizationAdmin { get; } = new Roles(2, "OrganizationAdmin", "User able to modify organzation, locations and users associated with that location");
            public static Roles SuperAdmin { get; } = new Roles(3, "SuperAdmin", "PermanentlyDeleted - Not available except to Super Admins");

            public static Roles FromId(int id)
            {
                return id switch
                {
                    0 => User,
                    1 => LocationAdmin,
                    2 => OrganizationAdmin,
                    3 => SuperAdmin,
                    _ => NoAccess
                    //throw new ArgumentOutOfRangeException(nameof(id), id, "Invalid status id")
                };
            }
        }
    }


}
