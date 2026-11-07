using System.Net;
using System.Net.Http.Json;
using Starter.Api.Tests.Infrastructure;
using Starter.Contracts;
using Starter.Contracts.Dashboard;
using Starter.Contracts.Notes;

namespace Starter.Api.Tests.Notes;

/// <summary>The owner can do everything with their notes, and invalid input is rejected with 400.</summary>
public sealed class NoteOperationsTests(StarterFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Owner_can_create_read_update_archive_restore_and_delete()
    {
        var (_, user) = await SignedInUserAsync();

        using var created = await SendAsync(HttpMethod.Post, "/api/notes", user.AccessToken, new CreateNoteRequest { Title = "  Groceries  ", Content = "Milk" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var note = (await created.Content.ReadFromJsonAsync<NoteDetails>(Cancellation))!;
        Assert.Equal("Groceries", note.Title);
        Assert.Equal($"/api/notes/{note.Id}", created.Headers.Location!.AbsolutePath);

        var read = await (await GetAsync($"/api/notes/{note.Id}", user.AccessToken)).Content.ReadFromJsonAsync<NoteDetails>(Cancellation);
        Assert.Equal("Milk", read!.Content);

        using var archived = await SendAsync(HttpMethod.Put, $"/api/notes/{note.Id}", user.AccessToken, new UpdateNoteRequest { Title = "Groceries", Content = "Milk, eggs", IsArchived = true });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal(1, (await DashboardAsync(user.AccessToken)).ArchivedNotes);
        Assert.Equal(0, (await ListAsync(user.AccessToken, archived: false)).TotalCount);
        Assert.Equal(1, (await ListAsync(user.AccessToken, archived: true)).TotalCount);

        using var restored = await SendAsync(HttpMethod.Put, $"/api/notes/{note.Id}", user.AccessToken, new UpdateNoteRequest { Title = "Groceries", Content = "Milk, eggs", IsArchived = false });
        Assert.False((await restored.Content.ReadFromJsonAsync<NoteDetails>(Cancellation))!.IsArchived);

        using var deleted = await SendAsync(HttpMethod.Delete, $"/api/notes/{note.Id}", user.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var gone = await GetAsync($"/api/notes/{note.Id}", user.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task List_is_paged_newest_first()
    {
        var (_, user) = await SignedInUserAsync();
        for (var i = 1; i <= 3; i++)
        {
            using var _ = await SendAsync(HttpMethod.Post, "/api/notes", user.AccessToken, new CreateNoteRequest { Title = $"Note {i}" });
        }

        var page1 = await ListAsync(user.AccessToken, archived: false, pageSize: 2);
        var page2 = await ListAsync(user.AccessToken, archived: false, page: 2, pageSize: 2);

        Assert.Equal(["Note 3", "Note 2"], page1.Items.Select(n => n.Title));
        Assert.True(page1.HasNextPage);
        Assert.Equal(["Note 1"], page2.Items.Select(n => n.Title));
        Assert.False(page2.HasNextPage);
    }

    public static TheoryData<string> InvalidBodies => new()
    {
        """{"title":""}""",
        """{"title":"   "}""",
        $$"""{"title":"{{new string('x', NoteLimits.TitleMaxLength + 1)}}"}""",
        $$"""{"title":"ok","content":"{{new string('x', NoteLimits.ContentMaxLength + 1)}}"}""",
        """{"content":"no title"}""",
        """{"title":"ok","id":"00000000-0000-0000-0000-000000000001"}""",
        """not json""",
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_note_is_rejected(string body)
    {
        var (_, user) = await SignedInUserAsync();

        using var response = await SendAsync(HttpMethod.Post, "/api/notes", user.AccessToken, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await DashboardAsync(user.AccessToken)).ActiveNotes);
    }

    [Fact]
    public async Task Validation_errors_name_the_field()
    {
        var (_, user) = await SignedInUserAsync();

        using var response = await SendAsync(HttpMethod.Post, "/api/notes", user.AccessToken, """{"title":""}""");

        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(Cancellation);
        Assert.Contains(problem!.Errors.Keys, key => key.Equals("Title", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Exception", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/notes?pageSize=1000")]
    [InlineData("/api/notes?pageSize=0")]
    [InlineData("/api/notes?page=0")]
    public async Task Out_of_range_paging_is_rejected(string path)
    {
        var (_, user) = await SignedInUserAsync();

        using var response = await GetAsync(path, user.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_request_body_is_rejected()
    {
        var (_, user) = await SignedInUserAsync();

        using var response = await SendAsync(HttpMethod.Post, "/api/notes", user.AccessToken,
            $$"""{"title":"big","content":"{{new string('x', 100_000)}}"}""");

        // Kestrel stops bodies over 64 KB with 413; the in-memory test server has no transport limits, so here the
        // API's own content length validation rejects it with 400. Either way nothing is stored.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest });
        Assert.Equal(0, (await DashboardAsync(user.AccessToken)).ActiveNotes);
    }

    private async Task<PagedResponse<NoteSummary>> ListAsync(string token, bool archived, int page = 1, int pageSize = 20) =>
        (await (await GetAsync($"/api/notes?archived={archived}&page={page}&pageSize={pageSize}", token)).Content
            .ReadFromJsonAsync<PagedResponse<NoteSummary>>(Cancellation))!;

    private async Task<DashboardResponse> DashboardAsync(string token) =>
        (await (await GetAsync("/api/dashboard", token)).Content.ReadFromJsonAsync<DashboardResponse>(Cancellation))!;
}
