using Microsoft.Data.SqlClient;

namespace Starter.E2E.Tests.Infrastructure;

/// <summary>
/// Removes what the tests created. Accounts are recognisable by their email domain; their notes, tokens and
/// sessions are removed with them. This runs only when E2E_SQL points at a test database - it is a test-side
/// tool, not an endpoint of the application.
/// </summary>
internal static class TestDataCleanup
{
    public static async Task RunAsync(E2ESettings settings, Mailbox mailbox)
    {
        var messages = await mailbox.SearchAsync($"to:\"@{E2ESettings.EmailDomain}\"");
        if (messages.Count > 0)
        {
            await mailbox.DeleteAsync(messages.Select(m => m.Id));
        }

        if (string.IsNullOrWhiteSpace(settings.CleanupConnectionString))
        {
            return;
        }

        await using var connection = new SqlConnection(settings.CleanupConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @users TABLE (Id nvarchar(450));
            INSERT INTO @users SELECT Id FROM [identity].AspNetUsers WHERE NormalizedEmail LIKE @pattern;
            DELETE FROM [app].Notes WHERE OwnerId IN (SELECT Id FROM @users);
            DELETE FROM [web].Sessions WHERE UserId IN (SELECT Id FROM @users);
            DELETE FROM [identity].OpenIddictTokens WHERE Subject IN (SELECT Id FROM @users);
            DELETE FROM [identity].OpenIddictAuthorizations WHERE Subject IN (SELECT Id FROM @users);
            DELETE FROM [identity].AspNetUsers WHERE Id IN (SELECT Id FROM @users);
            """;
        command.Parameters.AddWithValue("@pattern", "%@" + E2ESettings.EmailDomain.ToUpperInvariant());
        await command.ExecuteNonQueryAsync();
    }
}
