using OccupancyTracker.IRepository;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

public sealed class PublicContentService(IRepositorySessionFactory repositories)
{
    public async Task<string?> GetAsync(string page)
    {
        if (page is not ("privacy" or "terms")) return null;
        await using var session = await repositories.CreateAsync();
        var content = await session.MarkdownText.FirstOrDefaultAsync(
            x => x.PageName == page && x.TextIdentifier == "body" && x.CurrentStatus == Statuses.DataStatus.Active.Id);
        if (content?.MarkdownContent != null) return content.MarkdownContent;
        var fallback = await session.MarkdownText.FirstOrDefaultAsync(
            x => x.PageName == "" && x.TextIdentifier == "body" && x.CurrentStatus == Statuses.DataStatus.Active.Id);
        return fallback?.MarkdownContent;
    }
}
