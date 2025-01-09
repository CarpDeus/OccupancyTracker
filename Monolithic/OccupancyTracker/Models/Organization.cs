using Microsoft.IdentityModel.Tokens;
using Sqids;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
namespace OccupancyTracker.Models
{
    /// <summary>
    /// The top level indicating a group of locations
    /// </summary>
    public class Organization
    {
        /// <summary>
        /// Constructor of empty organization
        /// </summary>
        public Organization()
        {
            this.OrganizationAddress = new Address();
            this.PhoneNumber = new PhoneNumber();
        }

        /// <summary>
        /// Primary Key
        /// </summary>
        [Key]
        public long OrganizationId { get; set; }

        /// <summary>
        /// Human readable organization name
        /// </summary>
        [StringLength(256)]
        public string OrganizationName { get; set; }= string.Empty;

        /// <summary>
        /// Description of the organization
        /// </summary>
        [StringLength(1024)]
        public string OrganizationDescription { get; set; } = string.Empty;

        /// <summary>
        /// Can the organization used certain paid features
        /// </summary>
        public bool PaidClient { get; set; } = false;

        /// <summary>
        /// When the organization's paid status is valid until
        /// </summary>
        public DateOnly? PaidThroughDate { get; set; }

        /// <summary>
        /// Address of the organization
        /// </summary>
        public Address OrganizationAddress { get; set; }

        /// <summary>
        /// Contact phone number for the organization
        /// </summary>
        public PhoneNumber PhoneNumber { get; set; }

        /// <summary>
        /// Publicly accessible sqid for the organization
        /// </summary>
        [StringLength(36)]
        public string? OrganizationSqid { get; set; }

        /// <summary>
        /// Status of the data
        /// </summary>
        public int CurrentStatus { get; set; } = 0;

        /// <summary>
        /// Date the data was created
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Date the data was last modified. If null, not changed since creationg
        /// </summary>
        public DateTime? ModifiedDate { get; set; }

        /// <summary>
        /// UserInformationSqid of the user who created the data
        /// </summary>
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>
        /// UserInformationSqid of the user who last modified
        /// </summary>
        public string? ModifiedBy { get; set; }

        [NotMapped]
        public string CurrentStatusDescription { get { return Statuses.DataStatus.FromId(this.CurrentStatus).Name; } }

        /// <summary>
        ///  Number of locations associated with the organization
        /// </summary>
        public int LocationCount { get; set; } = 0;

        /// <summary>
        /// Default role for users in the organization
        /// </summary>
        public int DefaultRoleId { get; set; } = 0; // Default to User

        /// <summary>
        /// Determine if the organization matches the filter value
        /// </summary>
        /// <param name="filter">filter value</param>
        /// <returns>True if found, false if not</returns>
        public bool FilterCriteria(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }
            return Utility.FilterCriteria(this.OrganizationName, filter) ||
                Utility.FilterCriteria(this.OrganizationDescription, filter) ||
                this.OrganizationAddress.FilterCriteria(filter) ||
                this.PhoneNumber.FilterCriteria(filter);
        }

        
    }
}

