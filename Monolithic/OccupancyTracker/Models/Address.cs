using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OccupancyTracker.Models
{
    /// <summary>
    /// Standardized Address class to be used in all models
    /// </summary>
    [ComplexType]
    public class Address
    {

        /// <summary>
        /// Address line 1
        /// </summary>
        [StringLength(1024)]
        public string? AddressLine1 { get; set; } = string.Empty;

        /// <summary>
        /// Address line 2
        /// </summary>
        [StringLength(1024)]
        public string? AddressLine2 { get; set; } = string.Empty;
        /// <summary>
        /// City of the address
        /// </summary>
        [StringLength(512)]
        public string? City { get; set; } = string.Empty;
        /// <summary>
        /// State of the address
        /// </summary>
        [StringLength(512)]
        public string? State { get; set; } = string.Empty;
        /// <summary>
        /// Postal code of the address
        /// </summary>
        [StringLength(128)]
        public string? PostalCode { get; set; } = string.Empty;
        /// <summary>
        /// Country of the address
        /// </summary>
        [StringLength(256)]
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
            return Utility.FilterCriteria(this.AddressLine1, filter) ||
                Utility.FilterCriteria(this.AddressLine2, filter) ||
                Utility.FilterCriteria(this.City, filter) ||
                Utility.FilterCriteria(this.State, filter) ||
                Utility.FilterCriteria(this.PostalCode, filter) ||
                Utility.FilterCriteria(this.Country, filter) ;
        }


        
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
            return Utility.FilterCriteria(this.CountryCode, filter) ||
                Utility.FilterCriteria(this.Number, filter) ;
        }

    }
}