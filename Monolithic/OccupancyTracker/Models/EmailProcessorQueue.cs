using System.ComponentModel.DataAnnotations;

namespace OccupancyTracker.Models
{
    public class EmailProcessorQueue
    {
        [Key]
        public long EmailProcessorQueueId { get; set; }
        public string EmailProcessorData { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public long OrganizationId { get; set; }
        public long CreatedBy { get; set; }
    }

    public class EmailProcessorPointers
    {
        [Key]
        public long EmailProcessorPointersId { get; set; }
        public string EmailProcessorPointerName { get; set; } = string.Empty;
        public string EmailProcessorPointerDescription { get; set; } = string.Empty;
        public long EmailProcessorQueueId { get; set; } = 0;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    public class EmailProcessorHistory
    {
        [Key]
        public long EmailProcessorHistoryId { get; set; }
        public long EmailProcessorPointersId { get; set; }
        public long EmailProcessorQueueId { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public Int16 CurrentStatus { get; set; } = 0;
        public string OtherInformation { get; set; } = string.Empty;
        public string CurrentStatusDescription()
        {
            return CurrentStatus switch
            {
                0 => "Processing",
                1 => "Error",
                _ => "Unknown"
            };
        }
        public class EmailProcessorCurrent
        {
            public long EmailProcessorCurrentId { get; set; }
            public long EmailProcessorPointersId { get; set; }
            public long EmailProcessorQueueId { get; set; }
            public DateTime CreatedDate { get; set; } = DateTime.Now;
            public Int16 CurrentStatus { get; set; } = 0;
            public string CurrentStatusDescription()
            {
                return CurrentStatus switch
                {
                    0 => "Processing",
                    1 => "Error",
                    _ => "Unknown"
                };
            }
        }
    }
}
