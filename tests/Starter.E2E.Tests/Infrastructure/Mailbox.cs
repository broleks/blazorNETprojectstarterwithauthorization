using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Starter.E2E.Tests.Infrastructure;

/// <summary>Reads account emails from the local Mailpit inbox through its HTTP API.</summary>
public sealed partial class Mailbox(Uri mailpitUrl) : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private readonly HttpClient http = new() { BaseAddress = mailpitUrl };

    /// <summary>
    /// Waits until an email with the subject arrives for the address and returns the first https link in it.
    /// This is a condition wait with a deadline (email delivery is asynchronous), not a fixed sleep.
    /// </summary>
    public async Task<string> WaitForLinkAsync(string to, string subject, int expectedCount = 1)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            var messages = await SearchAsync($"to:\"{to}\" subject:\"{subject}\"");
            if (messages.Count >= expectedCount)
            {
                // Mailpit returns the newest message first.
                var message = await http.GetFromJsonAsync<MessageDetails>($"api/v1/message/{messages[0].Id}");
                return LinkRegex().Match(message!.Text).Value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No email '{subject}' arrived for {to} within {Timeout.TotalSeconds} seconds.");
            }

            await Task.Delay(PollInterval);
        }
    }

    public async Task<IReadOnlyList<MessageSummary>> SearchAsync(string query) =>
        (await http.GetFromJsonAsync<SearchResult>($"api/v1/search?query={Uri.EscapeDataString(query)}"))?.Messages ?? [];

    public async Task DeleteAsync(IEnumerable<string> ids)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "api/v1/messages") { Content = JsonContent.Create(new { IDs = ids.ToArray() }) };
        using var _ = await http.SendAsync(request);
    }

    public void Dispose() => http.Dispose();

    [GeneratedRegex(@"https://\S+")]
    private static partial Regex LinkRegex();

    public sealed record SearchResult([property: JsonPropertyName("messages")] List<MessageSummary> Messages);

    public sealed record MessageSummary([property: JsonPropertyName("ID")] string Id);

    public sealed record MessageDetails([property: JsonPropertyName("Text")] string Text);
}
