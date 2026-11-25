namespace OccupancyTracker.Service;

public static class LocalRedirect
{
    public static bool IsSafe(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' &&
        (url.Length == 1 || (url[1] != '/' && url[1] != '\\')) &&
        !url.Any(char.IsControl) && !url.Contains('\\');
}
