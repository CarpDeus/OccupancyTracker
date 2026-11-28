namespace OccupancyTracker.Service;

public sealed class ApiAddresses
{
    public Uri Public { get; }
    public Uri Internal { get; }

    public ApiAddresses(IConfiguration configuration, bool development)
    {
        Public = Parse(configuration["Api:BaseAddress"], "Api:BaseAddress", development, false);
        var internalAddress = configuration["Api:InternalBaseAddress"];
        Internal = internalAddress == null
            ? Public
            : Parse(internalAddress, "Api:InternalBaseAddress", development, true);
    }

    private static Uri Parse(string? address, string key, bool development, bool internalAddress)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             !(uri.Scheme == Uri.UriSchemeHttp && (internalAddress || development && uri.IsLoopback))))
            throw new InvalidOperationException($"{key} must use HTTPS; HTTP is allowed only for an explicitly configured internal address or localhost in development.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
}
