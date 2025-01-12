using Enyim.Caching;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.Models;
using SendGrid;

namespace OccupancyTracker.Service
{
    public class SendGridFactory : ISendGridFactory
    {
        private readonly IConfiguration _configuration;
        
        public SendGridFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SendGridClient CreateClient()
        {
            return new SendGridClient(_configuration["SendGridKey"]);
        }
    }
}
