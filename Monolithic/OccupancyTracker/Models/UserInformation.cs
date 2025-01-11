using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using FluentValidation;

namespace OccupancyTracker.Models
{

    public class UserInformationValidator : AbstractValidator<UserInformation>
    {
        public UserInformationValidator()
        {
            RuleFor(x => x.EmailAddress).EmailAddress().WithMessage("Email address is not valid.");
            RuleFor(x => x.FirstName).NotEmpty().MaximumLength(256).MinimumLength(2).WithMessage("First Name is required and must be between 2 and 256 characters long.");
            RuleFor(x => x.LastName).NotEmpty().MaximumLength(256).MinimumLength(2).WithMessage("Last Name is required and must be between 2 and 256 characters long.");
            RuleFor(x => x.ContactAddress).SetValidator(new AddressValidator());
            RuleFor(x => x.ContactPhoneNumber).SetValidator(new PhoneNumberValidator());
        }

        public Func<object, string, Task<IEnumerable<string>>> ValidateValue => async (model, propertyName) =>
        {
            var result = await ValidateAsync(ValidationContext<UserInformation>.CreateWithOptions((UserInformation)model, x => x.IncludeProperties(propertyName)));
            if (result.IsValid)
                return Array.Empty<string>();
            return result.Errors.Select(e => e.ErrorMessage);
        };
    }

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
        
        /// <summary>
        /// Last SSO Id used to log in for this email address
        /// </summary>
        public long UserSsoInformationIdLastLoggedIn { get; set; }

        /// <summary>
        /// Email address of the user
        /// </summary>
        [Required]
        [StringLength(320, ErrorMessage = "Email address must be 320 characters or less.", MinimumLength = 5)]
        public string EmailAddress { get; set; } = string.Empty;

        /// <summary>
        /// First name of the user
        /// </summary>
        [StringLength(256, ErrorMessage = "First Name must be between 2 and 256 characters.", MinimumLength = 2)]
        [Required]
        public string FirstName { get; set; }
        [StringLength(256, ErrorMessage = "Last Name must be between 2 and 256 characters.", MinimumLength = 2)]
        [Required]
        public string LastName { get; set; }
        [Required]
        public Address ContactAddress { get; set; }
        [Required]
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
