using Enyim.Caching;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.IService;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service
{
    public class MarkdownTextService : IMarkdownTextService
    {
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private readonly IMemcachedClient _memcachedClient;

        public MarkdownTextService(IDbContextFactory<OccupancyContext> contextFactory, IMemcachedClient memcachedClient)
        {
            _memcachedClient = memcachedClient;
            _contextFactory = contextFactory;
        }

        public async Task<MarkdownText> GetAsync(string PageName, string TextIdentifier)
        {
          /*  var key = $"MarkdownText_{PageName}_{TextIdentifier}";
            var markdownText = await _memcachedClient.GetAsync<MarkdownText>(key);
            if (markdownText == null)
            {*/
                using var context = _contextFactory.CreateDbContext();
               var  markdownText = await context.MarkdownText.FirstOrDefaultAsync(x => x.PageName == PageName && x.TextIdentifier == TextIdentifier && x.CurrentStatus == Statuses.DataStatus.Active.Id);
            if (markdownText == null)
            {
                markdownText = await context.MarkdownText.FirstOrDefaultAsync(x => x.PageName == string.Empty && x.TextIdentifier == TextIdentifier && x.CurrentStatus == Statuses.DataStatus.Active.Id);
            }
            //    if (markdownText != null)
            //    {
            //        await _memcachedClient.SetAsync(key, markdownText, TimeSpan.FromMinutes(5));
            //    }
            //}
            if (markdownText == null)
            {
                throw new KeyNotFoundException( $"Markdown Text Not Found - PageName: {PageName}, TextIdentifier: {TextIdentifier}");
            }
                return markdownText;
        }
        
    }
}
