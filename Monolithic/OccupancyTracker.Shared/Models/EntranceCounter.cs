using System.ComponentModel.DataAnnotations.Schema;

namespace OccupancyTracker.Models
{
    /// <summary>
    /// EntranceCounter is a counter for a specific entrance, used for storing changes in occupancy for a location
    /// </summary>
    public class EntranceCounter
    {
        /// <summary>
        /// Primary Key
        /// </summary>
        public long EntranceCounterId { get; set; }

        /// <summary>
        /// EntranceId this counter is associated with
        /// </summary>
        public long EntranceId { get; set; }

        /// <summary>
        /// LocationId this counter is associated with
        /// </summary>
        public long LocationId { get; set; }

        /// <summary>
        /// OrganizationId this counter is associated with
        /// </summary>
        public long OrganizationId { get; set; }

        /// <summary>
        /// Does this entrance counter require authentication. If true then the user attempting use the counter must be authenticated and authorized to the location
        /// </summary>
        public bool RequiresAuthentication { get; set; } = false;

        /// <summary>
        /// Public facing id for the entrance counter
        /// </summary>
        public string EntranceCounterSqid { get; set; } = string.Empty;

        /// <summary>
        /// Status of the data
        /// </summary>
        public int CurrentStatus { get; set; } = 0;

        /// <summary>
        /// Date the data was created
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Date the data was last modified. If null, not changed since creationg
        /// </summary>
        public DateTime? ModifiedDate { get; set; }

        /// <summary>
        /// UserInformationSqid of the user who created the data
        /// </summary>
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>
        /// UserInformationSqid of the user who last modified
        /// </summary>
        public string? ModifiedBy { get; set; }


        /// <summary>
        /// Human readable status of the data
        /// </summary>
        public string CurrentStatusDescription { get { return Statuses.DataStatus.FromId(this.CurrentStatus).Name; } }


        /// <summary>
        /// Human Readable Name
        /// </summary>
        public string EntranceName { get; set; } = string.Empty;
    }
}
