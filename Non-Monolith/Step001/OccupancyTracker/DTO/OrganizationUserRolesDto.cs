using OccupancyTracker.Data.Models;
using System.ComponentModel.DataAnnotations;

namespace OccupancyTracker.DTO
{
    public class OrganizationUserRolesDto
    {

        public OrganizationUserRolesDto() { }

        public OrganizationUserRolesDto(Location location, long organizationUserId)
        {
            this.LocationSqid = location.LocationSqid;
            this.LocationName = location.LocationName;
            this.LocationId = location.LocationId;
            this.OrganizationWide = false;
            this.OrganizationUserId = organizationUserId;
            this.OrganizatonUserRoleId = null;
            this.RoleName = null;
            this.RemoveRole = false;
        }
        public OrganizationUserRolesDto(Location location, OrganizationUserRole organizationUserRole)
        {
            this.LocationSqid = location.LocationSqid;
            this.LocationName = location.LocationName;
            this.LocationId = location.LocationId;
            this.OrganizationWide = organizationUserRole.OrganizationWide;
            this.OrganizationUserId = organizationUserRole.OrganizationUserId;
            this.OrganizatonUserRoleId = organizationUserRole.OrganizationUserRoleId;
            this.RoleName = organizationUserRole.RoleName;
            this.RemoveRole = false;
        }
        public OrganizationUserRolesDto(Location location, string roleName, bool organizationWide, long organizationUserId) 
        {
            this.LocationSqid = location.LocationSqid;
            this.LocationName = location.LocationName;
            this.LocationId = location.LocationId;
            this.OrganizationWide = organizationWide;
            this.OrganizationUserId = organizationUserId;
            this.OrganizatonUserRoleId = null;
            this.RoleName = roleName;
            this.RemoveRole = false;
        }

        public string LocationSqid { get; set; }
        public string LocationName { get; set; }
        public long LocationId { get; set; }
        public bool OrganizationWide { get; set; }
        public long OrganizationUserId { get; set; }
        public long? OrganizatonUserRoleId { get; set; }
        public string? RoleName { get; set; }
        public bool RemoveRole { get; set; } = false;
    }
}
