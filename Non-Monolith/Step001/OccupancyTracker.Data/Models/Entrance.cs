using System.Text.Json.Serialization;
using FluentValidation;

namespace OccupancyTracker.Data.Models
{
    public class EntranceValidator : AbstractValidator<Entrance>
    {
        public EntranceValidator()
        {
            RuleFor(x => x.EntranceName).NotEmpty().MaximumLength(256).WithMessage("Entrance Name is required and must be less than 256 characters long.");
            RuleFor(x => x.EntranceDescription).MaximumLength(1024).WithMessage("Entrance Description must be less than 1024 characters long.");
        }
        /// <summary>
        /// Validate a single property of the model
        /// </summary>
        public Func<object, string, Task<IEnumerable<string>>> ValidateValue => async (model, propertyName) =>
        {
            var result = await ValidateAsync(ValidationContext<Entrance>.CreateWithOptions((Entrance)model, x => x.IncludeProperties(propertyName)));
            if (result.IsValid)
                return Array.Empty<string>();
            return result.Errors.Select(e => e.ErrorMessage);
        };
    }

    /// <summary>
    /// Entrance is a point of entry to a location
    /// </summary>
    public class Entrance
    {
        /// <summary>
        /// Primary Key
        /// </summary>
        public long EntranceId { get; set; }

        /// <summary>
        /// Human Readable Name
        /// </summary>
        public string EntranceName { get; set; } = string.Empty;

        /// <summary>
        /// Description of the entrance
        /// </summary>
        public string EntranceDescription { get; set; } = string.Empty;

        /// <summary>
        /// Public facing entrance identifier
        /// </summary>
        public string EntranceSqid { get; set; } = string.Empty;

        /// <summary>
        /// The current entrance counter public facing identifier
        /// </summary>
        public string EntranceCounterInstanceSqid { get; set; } = string.Empty;

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


        /// <summary>
        /// FK to Location
        /// </summary>
        public long LocationId { get; set; }

        /// <summary>
        /// FK to Organization
        /// </summary>
        public long OrganizationId { get; set; }

        /// <summary>
        /// Used for determinig if a location should be displayed based on a filter
        /// </summary>
        /// <param name="filter">search string. if not provided, location will be displayed</param>
        /// <returns>True if match, False if not</returns>
        public bool FilterCriteria(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }
            return Utility.FilterCriteria(EntranceName, filter) ||
             Utility.FilterCriteria(EntranceDescription, filter);
        }



    }
}
