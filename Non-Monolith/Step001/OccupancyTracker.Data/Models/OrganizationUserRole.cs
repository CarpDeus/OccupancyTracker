//using OccupancyTracker.Components.Admin.Pages;

namespace OccupancyTracker.Data.Models
{
    public class OrganizationUserRole
    {
        public long OrganizationUserRoleId { get; set; }
        public string RoleName { get; set; }
        public bool OrganizationWide { get; set; } = false;

        // public OrganizationUser OrganizationUser { get; set; }
        public long OrganizationUserId { get; set; }
        public Location? Location { get; set; }
        public long? LocationId { get; set; }

    }
}
