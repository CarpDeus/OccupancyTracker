using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OccupancyTracker.Models
{
    /// <summary>
    /// Log for invalid security attempts.
    /// </summary>
    public class InvalidSecurityAttempt
    {
        /// <summary>
        /// Primary Key
        /// </summary>
        [Key]
        public long InvalidSecurityAttemptId { get; set; }

        /// <summary>
        /// Id of the user who attempted access
        /// </summary>
        public long UserInformationId { get; set; }

        public string InvalidSecurityAttemptSqid { get; set; } = string.Empty;

        /// <summary>
        /// Date the attempt was logged
        /// </summary>
        public DateTime AttemptLogged { get; set; }

        /// <summary>
        /// OrganizationId the user attempted to access
        /// </summary>
        public long? OrganizationId { get; set; }

        /// <summary>
        /// LocationId the user attempted to access
        /// </summary>
        public long? LocationId { get; set; }

        /// <summary>
        /// EntranceId the user attempted to access
        /// </summary>
        public long? EntranceId { get; set; }

        /// <summary>
        /// IP Address the attempt was made from
        /// </summary>
        public string? IpAddress { get; set; }

        /// <summary>
        /// More information about the attempt 
        /// </summary>
        public string AdditionalAttemptInformation { get; set; } = string.Empty;
    }
}
