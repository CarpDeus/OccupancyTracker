using OccupancyTracker.DTO;
using OccupancyTracker.Models;
using OccupancyTracker.Service;

namespace OccupancyTracker.IService
{
    public interface IMarkdownTextService
    {
        Task<MarkdownText>  GetAsync(string PageName, string TextIdentifier);
        
    }
}
