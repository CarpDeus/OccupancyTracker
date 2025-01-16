using System.ComponentModel.DataAnnotations;

namespace OccupancyTracker.Data.Models
{
    /// <summary>
    /// Log of occupancy changes
    /// </summary>
    public class OccupancyLog
    {
        /// <summary>
        /// Primary Key
        /// </summary>
        [Key]
        public long OccupancyLogId { get; set; }
        /// <summary>
        /// Organization id the change is logged for
        /// </summary>
        [Required]
        public long OrganizationId { get; set; }
        /// <summary>
        /// Location id the change is logged for
        /// </summary>
        [Required]
        public long LocationId { get; set; }
        /// <summary>
        /// Entrance id the change is logged for
        /// </summary>
        [Required]
        public long EntranceId { get; set; }
        /// <summary>
        /// EntranceCounter id logging the change
        /// </summary>
        [Required]
        public long EntranceCounterId { get; set; }

        /// <summary>
        /// Number of people logged as changed. Positive for people entering, negative for people leaving
        /// </summary>
        public int LoggedChange { get; set; }

        /// <summary>
        /// Current occupancy after the change
        /// </summary>
        public int CurrentOccupancy { get; set; }

        /// <summary>
        /// Date change was logged
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
