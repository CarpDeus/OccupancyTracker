using FluentValidation;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.InteropServices;

namespace OccupancyTracker.Data.Models
{
    public class AddressValidator : AbstractValidator<Address>
    {
        public AddressValidator()
        {
            RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(1024).MinimumLength(2).WithMessage("Address Line 1 is required and must be between 2 and 1024 characters long.");
            RuleFor(x => x.AddressLine2).MaximumLength(1024).WithMessage("Address Line 2 must be less than 1024 characters long.");
            RuleFor(x => x.City).NotEmpty().MaximumLength(512).MinimumLength(2).WithMessage("City is required and must be between 2 and 512 characters long.");
            RuleFor(x => x.State).NotEmpty().MaximumLength(512).MinimumLength(2).WithMessage("State is required and must be between 2 and 512 characters long.");
            RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(128).MinimumLength(2).WithMessage("Postal Code is required and must be between 2 and 128 characters long.");
            RuleFor(x => x.Country).NotEmpty().MaximumLength(256).MinimumLength(2).WithMessage("Country is required and must be between 2 and 256 characters long.");
        }

        public Func<object, string, Task<IEnumerable<string>>> ValidateValue => async (model, propertyName) =>
        {
            var result = await ValidateAsync(ValidationContext<Address>.CreateWithOptions((Address)model, x => x.IncludeProperties(propertyName)));
            if (result.IsValid)
                return Array.Empty<string>();
            return result.Errors.Select(e => e.ErrorMessage);
        };
    }


    /// <summary>
    /// Standardized Address class to be used in all models
    /// </summary>
    [ComplexType]
    public class Address
    {

        /// <summary>
        /// Address line 1
        /// </summary>
        [StringLength(1024, ErrorMessage = "Address Line 1 must be between 2 and 1024 characters.", MinimumLength = 2)]
        [Required]
        public string? AddressLine1 { get; set; } = string.Empty;

        /// <summary>
        /// Address line 2
        /// </summary>
        [StringLength(1024)]
        public string? AddressLine2 { get; set; } = string.Empty;
        /// <summary>
        /// City of the address
        /// </summary>
        [StringLength(512, ErrorMessage = "City must be between 2 and 512 characters.", MinimumLength = 2)]
        [Required]
        public string? City { get; set; } = string.Empty;
        /// <summary>
        /// State of the address
        /// </summary>
        [StringLength(512, ErrorMessage = "State must be between 2 and 512 characters.", MinimumLength = 2)]
        [Required]
        public string? State { get; set; } = string.Empty;
        /// <summary>
        /// Postal code of the address
        /// </summary>
        [StringLength(128, ErrorMessage = "Postal Code must be between 2 and 128 characters.", MinimumLength = 2)]
        [Required]
        public string? PostalCode { get; set; } = string.Empty;
        /// <summary>
        /// Country of the address
        /// </summary>
        [StringLength(256, ErrorMessage = "Country must be between 2 and 512 characters.", MinimumLength = 2)]
        [Required]
        public string? Country { get; set; } = string.Empty;


        /// <summary>
        /// Determine if the address matches the filter value
        /// </summary>
        /// <param name="filter">string being searched for</param>
        /// <returns>True if found, false if not</returns>
        internal bool FilterCriteria(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }
            return Utility.FilterCriteria(AddressLine1, filter) ||
                Utility.FilterCriteria(AddressLine2, filter) ||
                Utility.FilterCriteria(City, filter) ||
                Utility.FilterCriteria(State, filter) ||
                Utility.FilterCriteria(PostalCode, filter) ||
                Utility.FilterCriteria(Country, filter);
        }



    }

    public class PhoneNumberValidator : AbstractValidator<PhoneNumber>
    {
        public PhoneNumberValidator()
        {
            RuleFor(x => x.CountryCode).NotEmpty().MaximumLength(8).WithMessage("Country Code is required and must be less than 8 characters long.");
            RuleFor(x => x.Number).NotEmpty().MaximumLength(32).WithMessage("Phone Number is required and must be less than 32 characters long.");
        }
        public Func<object, string, Task<IEnumerable<string>>> ValidateValue => async (model, propertyName) =>
        {
            var result = await ValidateAsync(ValidationContext<PhoneNumber>.CreateWithOptions((PhoneNumber)model, x => x.IncludeProperties(propertyName)));
            if (result.IsValid)
                return Array.Empty<string>();
            return result.Errors.Select(e => e.ErrorMessage);
        };
    }

    /// <summary>
    /// Standardized Phone Number class to be used in all models
    /// </summary>
    [ComplexType]
    public class PhoneNumber
    {
        // TODO: ReplaceAsync with list of country codes
        /// <summary>
        /// Country code of the phone number
        /// </summary>
        public string? CountryCode { get; set; }

        /// <summary>
        /// Phone number
        /// </summary>
        public string? Number { get; set; }

        // TODO: Add phone type (landline, mobile, etc)

        /// <summary>
        /// Determine if the phone number matches the filter value
        /// </summary>
        /// <param name="filter">string being searched for</param>
        /// <returns>True if found, false if not</returns>
        internal bool FilterCriteria(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }
            return Utility.FilterCriteria(CountryCode, filter) ||
                Utility.FilterCriteria(Number, filter);
        }

    }
}