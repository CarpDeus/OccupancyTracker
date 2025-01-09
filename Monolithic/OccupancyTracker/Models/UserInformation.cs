using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace OccupancyTracker.Models
{
    public class UserInformation
    {

        public UserInformation()
        {
            this.ContactAddress = new Address();
            this.ContactPhoneNumber = new PhoneNumber();
        }
        public UserInformation(long userSsoInformationIdLastLoggedIn)
        {
            this.UserSsoInformationIdLastLoggedIn = userSsoInformationIdLastLoggedIn;
            this.ContactAddress = new Address();
            this.ContactPhoneNumber = new PhoneNumber();
        }
        [Key]
        public long UserInformationId { get; set; }
        public long UserSsoInformationIdLastLoggedIn { get; set; }
        public string EmailAddress { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public Address ContactAddress { get; set; }
        public PhoneNumber ContactPhoneNumber { get; set; }
        public string? UserInformationSqid { get; set; }
        public bool IsSuperAdmin { get; set; } = false;
        public bool HasCompletedRegistration { get; set; } = false;
        public bool BelongsToOrganization { get; set; } = false;

        public int CurrentStatusId { get; set; } = 0;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? ModifiedDate { get; set; }
        [StringLength(450)]
        public long CreatedBy { get; set; } = -1;
        [StringLength(450)]
        public long? ModifiedBy { get; set; }
        [NotMapped]
        public string CurrentStatusDescription { get { return Statuses.DataStatus.FromId(this.CurrentStatusId).Name; } }

        //public List<UserSsoInformation> UserSsoInformationList { get; set; }
        

    }
}
