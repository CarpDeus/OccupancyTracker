using System.ComponentModel.DataAnnotations;

namespace OccupancyTracker.Models
{
    public class OrganizationUser
    {
        [Key]
        public long OrganizationUsersId { get; set; }
        
        //public UserInformation UserInformation { get; set; }
        public long UserInformationId { get; set; }
        
        //public Organization Organization { get; set; }
        public long OrganizationId { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? ModifiedDate { get; set; }
        [StringLength(450)]
        public string CreatedBy { get; set; } = string.Empty;
        [StringLength(450)]
        public string? ModifiedBy { get; set; }

        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public int UserStatusId { get; set; } = AuthorizationRecords.UserStatus.Approved.Id;

        

        public UserInformation UserInformation { get; set; }
    }
}
