using System.Text.Json;

namespace Starter.Api.Tests.Infrastructure;

/// <summary>Reads the ECS JSON log files written by an application under test.</summary>
public static class LogFiles
{
    /// <summary>All log documents of <paramref name="service"/> that match, waiting briefly because the request
    /// summary is written as the response completes.</summary>
    public static async Task<IReadOnlyList<JsonElement>> WaitForAsync(string folder, string service, Func<JsonElement, bool> match, int expected = 1)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var found = Read(folder, service).Where(match).ToList();
            if (found.Count >= expected || DateTime.UtcNow > deadline)
            {
                return found;
            }

            await Task.Delay(100);
        }
    }

    public static IEnumerable<JsonElement> Read(string folder, string service)
    {
        var directory = Path.Combine(folder, service);
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            // The application keeps the file open; share it for reading.
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { Length: > 0 } line)
            {
                yield return JsonDocument.Parse(line).RootElement.Clone();
            }
        }
    }

    public static string AllText(string folder) =>
        string.Join("\n", Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).Select(file =>
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return new StreamReader(stream).ReadToEnd();
            })
            : []);

    public static string? Get(this JsonElement document, string path)
    {
        // ECS documents mix nested objects and dotted keys, e.g. "http": { "response.status_code": 404 }.
        if (document.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (document.TryGetProperty(path, out var direct))
        {
            return direct.ValueKind == JsonValueKind.String ? direct.GetString() : direct.ToString();
        }

        var parts = path.Split('.');
        for (var i = 1; i < parts.Length; i++)
        {
            if (document.TryGetProperty(string.Join('.', parts[..i]), out var child) &&
                child.Get(string.Join('.', parts[i..])) is { } value)
            {
                return value;
            }
        }

        return null;
    }
}
