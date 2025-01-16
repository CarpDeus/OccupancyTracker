using System;

namespace OccupancyTracker.Data.Models
{
    public static class Utility
    {
        /// <summary>
        /// Filter criteria for a single string value
        /// </summary>
        /// <param name="value">Value being checked</param>
        /// <param name="filter">filter being looked for</param>
        /// <returns>True if found, false if not</returns>
        public static bool FilterCriteria(string? value, string filter)
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }
            return value.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }
    }
}
