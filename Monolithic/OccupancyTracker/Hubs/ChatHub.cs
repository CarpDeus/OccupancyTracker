using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace OccupancyTracker.Hubs
{
    public class ChatHub : Hub
    {
        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }

        public async Task UpdateOccupancy(string locationSqid, int currentOccupancy, int warningOccupancy)
        {
            await Clients.All.SendAsync("UpdateOccupancy",locationSqid, currentOccupancy, warningOccupancy);
        }

    }
}
