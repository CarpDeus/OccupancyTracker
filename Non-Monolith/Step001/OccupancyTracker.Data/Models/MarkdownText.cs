using System.Text.Json.Serialization;

namespace OccupancyTracker.Data.Models
{
    /// <summary>
    /// Storage of MarkdownText data
    /// </summary>
    public class MarkdownText
    {
        /// <summary>
        /// Primary Key
        /// </summary>
        public long MarkdownTextId { get; set; }

        /// <summary>
        /// URI Page Identifier
        /// </summary>
        public string PageName { get; set; } = string.Empty;

        /// <summary>
        /// Identifier of the block of text
        /// </summary>
        public string TextIdentifier { get; set; } = string.Empty;

        /// <summary>
        /// Public facing entrance identifier
        /// </summary>
        public string MarkdownContent { get; set; } = string.Empty;

        /// <summary>
        /// Status of the entrance
        /// </summary>
        public int CurrentStatus { get; set; } = 0;

        /// <summary>
        /// Date the entrance data was created
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Date the entrance data was last modified
        /// </summary>
        public DateTime? ModifiedDate { get; set; }

        /// <summary>
        /// UserInformationSqid of the user who created the entrance
        /// </summary>
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>
        /// UserInformationSqid of the user who last modified the data
        /// </summary>
        public string? ModifiedBy { get; set; }

        /// <summary>
        /// Human readable status of the data
        /// </summary>
        public string CurrentStatusDescription { get { return Statuses.DataStatus.FromId(CurrentStatus).Name; } }


    }
}
