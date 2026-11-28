using System.Text.Json;
using System.Net.Http.Json;

namespace OccupancyTracker.Service;

public sealed class ApiReadinessClient(HttpClient http, ILogger<ApiReadinessClient> logger)
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync("api/ready", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;
            var status = await response.Content.ReadFromJsonAsync<ReadinessStatus>(cancellationToken);
            return status?.Ready == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException ||
                                   ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "API readiness check failed.");
            return false;
        }
    }

    private sealed record ReadinessStatus(bool Ready);
}
