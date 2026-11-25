using System.Runtime.CompilerServices;

namespace OccupancyTracker.Models
{
    public class Statuses
    {
        public record DataStatus(int Id, string Name, string Description)
        {
            public static DataStatus Active { get; } = new DataStatus(0, "Active", "Available");
            public static DataStatus Deleted { get; } = new DataStatus(1, "Deleted", "Only shows up to Administrators");
            public static DataStatus PermanentlyDeleted { get; } = new DataStatus(2, "Permanently Deleted", "Permanently Deleted - Not available except to Super Admins");

            public static DataStatus FromId(int id)
            {
                return id switch
                {
                    0 => Active,
                    1 => Deleted,
                    2 => PermanentlyDeleted,
                    _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Invalid status id")
                };
            }

        

            public static int[] ValidChangeStates(int fromStatusId)
            {
                switch (fromStatusId)
                {
                    case 0: return new int[] { 1 };
                    case 1: return new int[] { 0, 2 };
                    case 2: return new int[] { 1 };
                    default: throw new Exception($"{fromStatusId} is not valid.");
                }
            }
        }
    }
}
