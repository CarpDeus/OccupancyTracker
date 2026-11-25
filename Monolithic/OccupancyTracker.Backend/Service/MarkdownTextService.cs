using Enyim.Caching;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.IService;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service
{
    /// <summary>
    /// Service for handling Markdown text operations.
    /// </summary>
    public class MarkdownTextService : IMarkdownTextService
    {
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private readonly IMemcachedClient _memcachedClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="MarkdownTextService"/> class.
        /// </summary>
        /// <param name="contextFactory">The context factory for creating database contexts.</param>
        /// <param name="memcachedClient">The Memcached client for caching.</param>
        public MarkdownTextService(IDbContextFactory<OccupancyContext> contextFactory, IMemcachedClient memcachedClient)
        {
            _memcachedClient = memcachedClient;
            _contextFactory = contextFactory;
        }

        /// <summary>
        /// Gets the Markdown text for a specific page and text identifier.
        /// </summary>
        /// <param name="PageName">The name of the page.</param>
        /// <param name="TextIdentifier">The text identifier.</param>
        /// <returns>The Markdown text object.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the Markdown text is not found.</exception>
        public async Task<MarkdownText> GetAsync(string PageName, string TextIdentifier)
        {
            /* var key = $"MarkdownText_{PageName}_{TextIdentifier}";
            var markdownText = await _memcachedClient.GetAsync<MarkdownText>(key);
            if (markdownText == null)
            { */
            using var context = _contextFactory.CreateDbContext();
            var markdownText = await context.MarkdownText.FirstOrDefaultAsync(x => x.PageName == PageName && x.TextIdentifier == TextIdentifier && x.CurrentStatus == Statuses.DataStatus.Active.Id);
            if (markdownText == null)
            {
                markdownText = await context.MarkdownText.FirstOrDefaultAsync(x => x.PageName == string.Empty && x.TextIdentifier == TextIdentifier && x.CurrentStatus == Statuses.DataStatus.Active.Id);
            }
            // if (markdownText != null)
            // {
            //     await _memcachedClient.SetAsync(key, markdownText, TimeSpan.FromMinutes(5));
            // }
            // }
            if (markdownText == null)
            {
                throw new KeyNotFoundException($"Markdown Text Not Found - PageName: {PageName}, TextIdentifier: {TextIdentifier}");
            }
            return markdownText;
        }
    }
}
