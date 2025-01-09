using Microsoft.Identity.Client;
using OccupancyTracker.Models;

namespace OccupancyTracker.DTO
{
    public class EntranceDto 
    {
        public Entrance Entrance { get; set; }
        public Organization Organization { get; set; }
        public Location Location { get; set; }
    }
}
