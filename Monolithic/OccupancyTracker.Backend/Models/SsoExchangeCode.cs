using System.ComponentModel.DataAnnotations;

namespace OccupancyTracker.Models;

public sealed class SsoExchangeCode
{
    [Key, MaxLength(64)]
    public string CodeHash { get; set; } = "";
    [Required, MaxLength(64)]
    public string Challenge { get; set; } = "";
    [Required]
    public string ProtectedToken { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
}
