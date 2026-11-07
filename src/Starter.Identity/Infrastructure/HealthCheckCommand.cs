namespace Starter.Identity.Infrastructure;

/// <summary>Implements <c>--health-check &lt;url&gt;</c> for the container health check (no curl in the image). Certificate validation stays on.</summary>
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
