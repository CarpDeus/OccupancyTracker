using Enyim.Caching;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.Models;
using SendGrid;

namespace OccupancyTracker.Service
{
    /// <summary>
    /// Factory for creating SendGridClient instances.
    /// </summary>
    public class SendGridFactory : ISendGridFactory
    {
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="SendGridFactory"/> class.
        /// </summary>
        /// <param name="configuration">The configuration to use for creating SendGridClient.</param>
        public SendGridFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Creates a new instance of <see cref="SendGridClient"/>.
        /// </summary>
        /// <returns>A new instance of <see cref="SendGridClient"/>.</returns>
        public SendGridClient CreateClient()
        {
            return new SendGridClient(_configuration["SendGridKey"]);
        }
    }
}
