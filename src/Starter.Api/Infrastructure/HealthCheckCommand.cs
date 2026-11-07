namespace Starter.Api.Infrastructure;

/// <summary>
/// Implements <c>--health-check &lt;url&gt;</c> so the container image can check itself without curl or wget.
/// Used by the Docker Compose health check; App Service uses its own HTTP health probe instead.
/// Certificate validation stays enabled.
/// </summary>
internal static class HealthCheckCommand
{
    public static async Task<int> RunAsync(string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await client.GetAsync(new Uri(url));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"Health check failed: {ex.Message}");
            return 1;
        }
    }
}
