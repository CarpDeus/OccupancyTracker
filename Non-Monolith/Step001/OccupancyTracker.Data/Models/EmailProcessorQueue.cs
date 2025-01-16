using System.ComponentModel.DataAnnotations;

namespace OccupancyTracker.Data.Models
{

    /// <summary>
    /// Represents a queue for processing emails.
    /// </summary>
    public class EmailProcessorQueue
    {
        /// <summary>
        /// Gets or sets the unique identifier for the email processor queue.
        /// </summary>
        [Key]
        public long EmailProcessorQueueId { get; set; }

        /// <summary>
        /// Gets or sets the data associated with the email processor.
        /// </summary>
        public string EmailProcessorData { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the date when the email processor queue was created.
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Gets or sets the organization identifier associated with the email processor queue.
        /// </summary>
        public long OrganizationId { get; set; }

        /// <summary>
        /// Gets or sets the identifier of the user who created the email processor queue.
        /// </summary>
        public long CreatedBy { get; set; }
    }

    /// <summary>
    /// Represents pointers for the email processor.
    /// </summary>
    public class EmailProcessorPointers
    {
        /// <summary>
        /// Gets or sets the unique identifier for the email processor pointers.
        /// </summary>
        [Key]
        public long EmailProcessorPointersId { get; set; }

        /// <summary>
        /// Gets or sets the name of the email processor pointer.
        /// </summary>
        public string EmailProcessorPointerName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the description of the email processor pointer.
        /// </summary>
        public string EmailProcessorPointerDescription { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the queue identifier associated with the email processor pointer.
        /// </summary>
        public long EmailProcessorQueueId { get; set; } = 0;

        /// <summary>
        /// Gets or sets the date when the email processor pointer was created.
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Represents the history of the email processor.
    /// </summary>
    public class EmailProcessorHistory
    {
        /// <summary>
        /// Gets or sets the unique identifier for the email processor history.
        /// </summary>
        [Key]
        public long EmailProcessorHistoryId { get; set; }

        /// <summary>
        /// Gets or sets the pointer identifier associated with the email processor history.
        /// </summary>
        public long EmailProcessorPointersId { get; set; }

        /// <summary>
        /// Gets or sets the queue identifier associated with the email processor history.
        /// </summary>
        public long EmailProcessorQueueId { get; set; }

        /// <summary>
        /// Gets or sets the date when the email processor history was created.
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Gets or sets the current status of the email processor.
        /// </summary>
        public short CurrentStatus { get; set; } = 0;

        /// <summary>
        /// Gets or sets other information related to the email processor history.
        /// </summary>
        public string OtherInformation { get; set; } = string.Empty;

        /// <summary>
        /// Gets the description of the current status.
        /// </summary>
        /// <returns>A string representing the current status description.</returns>
        public string CurrentStatusDescription()
        {
            return CurrentStatus switch
            {
                0 => "Processing",
                1 => "Error",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Represents the current state of the email processor.
        /// </summary>
        public class EmailProcessorCurrent
        {
            /// <summary>
            /// Gets or sets the unique identifier for the current email processor.
            /// </summary>
            public long EmailProcessorCurrentId { get; set; }

            /// <summary>
            /// Gets or sets the pointer identifier associated with the current email processor.
            /// </summary>
            public long EmailProcessorPointersId { get; set; }

            /// <summary>
            /// Gets or sets the queue identifier associated with the current email processor.
            /// </summary>
            public long EmailProcessorQueueId { get; set; }

            /// <summary>
            /// Gets or sets the date when the current email processor was created.
            /// </summary>
            public DateTime CreatedDate { get; set; } = DateTime.Now;

            /// <summary>
            /// Gets or sets the current status of the email processor.
            /// </summary>
            public short CurrentStatus { get; set; } = 0;

            /// <summary>
            /// Gets the description of the current status.
            /// </summary>
            /// <returns>A string representing the current status description.</returns>
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
