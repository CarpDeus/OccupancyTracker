using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace OccupancyTracker.Hubs
{
    public class OccupancyTrackerHub : Hub
    {

        public async Task JoinLocationAsync(string locationSqid)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, locationSqid);
        }

        public async Task LeaveLocationAsync(string locationSqid)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, locationSqid);
        }

        public async Task UpdateOccupancy(string locationSqid, int currentOccupancy, int warningOccupancy)
        {
            await Clients.Group(locationSqid).SendAsync("UpdateOccupancy",locationSqid, currentOccupancy, warningOccupancy);
        }

    }
}
