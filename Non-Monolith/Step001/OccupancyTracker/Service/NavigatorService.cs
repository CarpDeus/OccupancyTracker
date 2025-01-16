using Microsoft.AspNetCore.Components;

namespace OccupancyTracker.Service
{
    /// <summary>
    /// Service to handle navigation within the application.
    /// </summary>
    public class NavigatorService
    {
        /// <summary>
        /// Gets or sets the NavigationManager instance used for navigation.
        /// </summary>
        internal NavigationManager NavigationManager { get; set; }
    }
}
