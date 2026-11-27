using OccupancyTracker.IRepository;
using OccupancyTracker.IService;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service
{
    /// <summary>
    /// Service for handling Markdown text operations.
    /// </summary>
    public class MarkdownTextService : IMarkdownTextService
    {
        private readonly IRepositorySessionFactory _repositoryFactory;
        private readonly ICacheRepository _cacheRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="MarkdownTextService"/> class.
        /// </summary>
        /// <param name="repositoryFactory">The factory for creating repository sessions.</param>
        /// <param name="cacheRepository">The cache repository.</param>
        public MarkdownTextService(IRepositorySessionFactory repositoryFactory, ICacheRepository cacheRepository)
        {
            _cacheRepository = cacheRepository;
            _repositoryFactory = repositoryFactory;
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
            using var context = _repositoryFactory.Create();
            var markdownText = await context.MarkdownText.FirstOrDefaultAsync(x => x.PageName == PageName && x.TextIdentifier == TextIdentifier && x.CurrentStatus == Statuses.DataStatus.Active.Id);
            if (markdownText == null)
            {
                markdownText = await context.MarkdownText.FirstOrDefaultAsync(x => x.PageName == string.Empty && x.TextIdentifier == TextIdentifier && x.CurrentStatus == Statuses.DataStatus.Active.Id);
            }
            if (markdownText == null)
            {
                throw new KeyNotFoundException($"Markdown Text Not Found - PageName: {PageName}, TextIdentifier: {TextIdentifier}");
            }
            return markdownText;
        }
    }
}
