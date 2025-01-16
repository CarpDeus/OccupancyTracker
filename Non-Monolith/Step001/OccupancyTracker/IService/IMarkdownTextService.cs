using OccupancyTracker.Data.Models;
using OccupancyTracker.DTO;
using OccupancyTracker.Service;

namespace OccupancyTracker.IService
{
    public interface IMarkdownTextService
    {
        /// <summary>
        /// Get Markdown from the database for a specific page and text identifier
        /// </summary>
        /// <param name="PageName">Name of the page</param>
        /// <param name="TextIdentifier">Text identifier</param>
        /// <returns>Markdown text object</returns>
        Task<MarkdownText>  GetAsync(string PageName, string TextIdentifier);
        
    }
}
