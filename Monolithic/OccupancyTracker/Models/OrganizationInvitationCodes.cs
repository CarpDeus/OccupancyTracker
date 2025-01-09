using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OccupancyTracker.Models
{
    public class OrganizationInvitationCodes
    {
        [Key]
        public long OrganizationInvitationCodeId { get; set; }
        public long OrganizationId { get; set; }
        [StringLength(256)]
        public string EmailAddress { get; set; }=string.Empty;
        /// <summary>
        /// This is a JSON object that contains authorization information for the user once they are created
        /// </summary>
        public string PostRegistrationRoleInformation { get; set; }=string.Empty;
        /// <summary>
        /// This is the Sqid of the OrganizationId and EnitityInvitationCodeId
        /// </summary>
        [StringLength(36)]
        public string? InvitationCode { get; set; } = string.Empty;

        public int CurrentStatus { get; set; } = 0;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? ModifiedDate { get; set; }
        [StringLength(450)]
        public string CreatedBy { get; set; } = string.Empty;
        [StringLength(450)]
        public string? ModifiedBy { get; set; }
        public DateOnly? InvitationRedeemed { get; set; }

        public string CurrentStatusDescription { get { return Statuses.DataStatus.FromId(this.CurrentStatus).Name; } }

    }
}
