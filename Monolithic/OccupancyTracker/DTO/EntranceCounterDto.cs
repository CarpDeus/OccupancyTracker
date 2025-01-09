using OccupancyTracker.Models;
using System.ComponentModel.DataAnnotations;

namespace OccupancyTracker.DTO
{
    public class EntranceCounterDto
    {

        public EntranceCounterDto(){}

        public EntranceCounterDto(EntranceCounter entranceCounter)
        {
            EntranceCounterId = entranceCounter.EntranceCounterId;
            EntranceId = entranceCounter.EntranceId;
            LocationId = entranceCounter.LocationId;
            OrganizationId = entranceCounter.OrganizationId;
            RequiresAuthentication = entranceCounter.RequiresAuthentication;
            EntranceCounterSqid = entranceCounter.EntranceCounterSqid;
            CurrentStatus = entranceCounter.CurrentStatus;
            
        }



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

        public int StartingCounter { get; set; } = 0;

        /// <summary>
        /// Human readable organization name
        /// </summary>
        [StringLength(256)]
        public string OrganizationName { get; set; } = string.Empty;


        /// <summary>
        /// Human readable name of the location
        /// </summary>
        [Required]
        [StringLength(256)]
        public string LocationName { get; set; } = string.Empty;

        /// <summary>
        /// Maximum occupancy of the location
        /// </summary>
        [Required]

        public int MaxOccupancy { get; set; } = 1;

        /// <summary>
        /// Current occupancy. Generally set using the entrance counters
        /// </summary>
        [Required] public int CurrentOccupancy { get; set; } = 0;

        /// <summary>
        /// Human Readable Name
        /// </summary>
        public string EntranceName { get; set; } = string.Empty;

        /// <summary>
        /// The point at which a location is close enough to full to trigger a warning
        /// </summary>
        [Required]
        [Compare("MaxOccupancy", ErrorMessage = "Threshold must be less than Max Occupancy")]
        public int OccupancyThresholdWarning { get; set; } = 0;

        /// <summary>
        /// Public facing entrance identifier
        /// </summary>
        public string EntranceSqid { get; set; } = string.Empty;

        /// <summary>
        /// Public facing location identifier
        /// </summary>
        public string? LocationSqid { get; set; }
        /// <summary>
        /// Publicly accessible sqid for the organization
        /// </summary>
        [StringLength(36)]
        public string? OrganizationSqid { get; set; }

    }
}
