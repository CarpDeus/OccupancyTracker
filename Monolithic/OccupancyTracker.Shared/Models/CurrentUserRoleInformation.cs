namespace OccupancyTracker.Models
{
    /// <summary>
    /// Class for storing the current user role claims
    /// </summary>
    public class CurrentUserRoleInformation
    {
        /// <summary>
        /// OrganizationSqid user has access to
        /// </summary>
        public string OrganizationSqid { get; set; }

        /// <summary>
        /// LocationSqid user has access to
        /// </summary>
        public string? LocationSqid { get; set; }

        /// <summary>
        /// Does this role apply to the entire organization
        /// </summary>
        public bool OrganizationWide { get; set; } = false;

        /// <summary>
        /// Role name
        /// </summary>
        public string RoleName { get; set; } 
    }
}
