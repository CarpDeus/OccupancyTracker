using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace OccupancyTracker.Hubs
{
    /// <summary>
    /// SignalR Hub for tracking occupancy of locations
    /// </summary>
    public class OccupancyTrackerHub : Hub
    {

        /// <summary>
        /// Join a location group
        /// </summary>
        /// <param name="locationSqid">Id of the location</param>
        /// <returns></returns>
        public async Task JoinLocationAsync(string locationSqid)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, locationSqid);
        }

        /// <summary>
        /// Leave a location group
        /// </summary>
        /// <param name="locationSqid">Id of the location</param>
        /// <returns></returns>
        public async Task LeaveLocationAsync(string locationSqid)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, locationSqid);
        }

        /// <summary>
        /// Update the occupancy of a location
        /// </summary>
        /// <param name="locationSqid">Id of the location</param>
        /// <param name="currentOccupancy">Current occupancy value</param>
        /// <param name="warningOccupancy">Warning theshhold value</param>
        /// <returns></returns>
        public async Task UpdateOccupancy(string locationSqid, int currentOccupancy, int warningOccupancy)
        {
            await Clients.Group(locationSqid).SendAsync("UpdateOccupancy",locationSqid, currentOccupancy, warningOccupancy);
        }

    }
}
