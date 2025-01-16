using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OccupancyTracker.Data.Models
{
    /// <summary>
    /// Represents the invitation codes for an organization.
    /// </summary>
    public class OrganizationInvitationCodes
    {
        /// <summary>
        /// Gets or sets the unique identifier for the organization invitation code.
        /// </summary>
        [Key]
        public long OrganizationInvitationCodeId { get; set; }

        /// <summary>
        /// Gets or sets the unique identifier for the organization.
        /// </summary>
        public long OrganizationId { get; set; }

        /// <summary>
        /// Gets or sets the email address associated with the invitation.
        /// </summary>
        [StringLength(256)]
        public string EmailAddress { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the JSON object that contains authorization information for the user once they are created.
        /// </summary>
        public string PostRegistrationRoleInformation { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the invitation code, which is a combination of the OrganizationId and EntityInvitationCodeId.
        /// </summary>
        [StringLength(36)]
        public string? InvitationCode { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current status of the invitation.
        /// </summary>
        public int CurrentStatus { get; set; } = 0;

        /// <summary>
        /// Gets or sets the date and time when the invitation was created.
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Gets or sets the date and time when the invitation was last modified.
        /// </summary>
        public DateTime? ModifiedDate { get; set; }

        /// <summary>
        /// Gets or sets the user who created the invitation.
        /// </summary>
        [StringLength(450)]
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the user who last modified the invitation.
        /// </summary>
        [StringLength(450)]
        public string? ModifiedBy { get; set; }

        /// <summary>
        /// Gets or sets the date when the invitation was redeemed.
        /// </summary>
        public DateOnly? InvitationRedeemed { get; set; }

        /// <summary>
        /// Gets the description of the current status.
        /// </summary>
        public string CurrentStatusDescription { get { return Statuses.DataStatus.FromId(CurrentStatus).Name; } }
    }
}
