using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace OccupancyTracker.Models
{
    public class UserSsoInformation
    {

        public UserSsoInformation()
        {
        }
        
        public long UserSsoInformationId { get; set; }
        public long? UserInformationId { get; set; }
        public long UserSsoInformationIdLastLoggedIn { get; set; } 
        public DateTime UserLastLoggedIn { get; set; }
        public string Auth0Identifier { get; set; } = string.Empty;
        public string? GivenName { get; set; }
        public string? Surname { get; set; }
        public string? Picture { get; set; }
        public string? EmailAddress { get; set; }
    }
}
