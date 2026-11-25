using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace OccupancyTracker.Models
{
    public class Profile
    {
        public Profile()
        {
            this.ProfileAddress = new Address();
            this.ProfilePhoneNumber = new PhoneNumber();
        }
        public Profile(string userId)
        {
            this.AppUserId = userId;
            this.ProfileAddress = new Address();
            this.ProfilePhoneNumber = new PhoneNumber();
        }

        public long ProfileId { get; set; }
        public string AppUserId { get; set; }= string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public Address ProfileAddress { get; set; }
        public PhoneNumber ProfilePhoneNumber { get; set; }
        public string? userInformationSqid { get; set; }
        public bool IsSuperAdmin { get; set; } = false;

        public int CurrentStatus { get; set; } = 0;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? ModifiedDate { get; set; }
        [StringLength(450)]
        public string CreatedBy { get; set; } = string.Empty;
        [StringLength(450)]
        public string? ModifiedBy { get; set; }
        [NotMapped]
        public string CurrentStatusDescription { get { return Statuses.DataStatus.FromId(this.CurrentStatus).Name; } }

    }

}
