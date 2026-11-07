using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Starter.Api.Infrastructure;
using Starter.Api.Tests.Infrastructure;
using Starter.Contracts;
using Starter.Contracts.Notes;

namespace Starter.Api.Tests.Notes;

/// <summary>Users can only ever see and change their own notes, whatever the request contains.</summary>
public sealed class NoteOwnershipTests(StarterFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Other_users_note_cannot_be_read_changed_or_deleted()
    {
        var (_, alice) = await SignedInUserAsync("Alice");
        var (_, mallory) = await SignedInUserAsync("Mallory");
        var note = await CreateNoteAsync(alice.AccessToken, "Alice's secret");

        using var read = await GetAsync($"/api/notes/{note.Id}", mallory.AccessToken);
        using var update = await SendAsync(HttpMethod.Put, $"/api/notes/{note.Id}", mallory.AccessToken,
            new UpdateNoteRequest { Title = "Hacked", Content = "Hacked", IsArchived = true });
        using var delete = await SendAsync(HttpMethod.Delete, $"/api/notes/{note.Id}", mallory.AccessToken);

        // 404 rather than 403: the API does not even confirm that the id exists.
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        // The note is untouched.
        var unchanged = await (await GetAsync($"/api/notes/{note.Id}", alice.AccessToken)).Content.ReadFromJsonAsync<NoteDetails>(Cancellation);
        Assert.Equal("Alice's secret", unchanged!.Title);
        Assert.False(unchanged.IsArchived);
    }

    [Fact]
    public async Task Lists_and_dashboard_contain_only_own_notes()
    {
        var (_, alice) = await SignedInUserAsync();
        var (_, bob) = await SignedInUserAsync();
        await CreateNoteAsync(alice.AccessToken, "Alice 1");
        await CreateNoteAsync(alice.AccessToken, "Alice 2");
        await CreateNoteAsync(bob.AccessToken, "Bob 1");

        var bobsList = await (await GetAsync("/api/notes", bob.AccessToken)).Content.ReadFromJsonAsync<PagedResponse<NoteSummary>>(Cancellation);
        var bobsDashboard = await (await GetAsync("/api/dashboard", bob.AccessToken)).Content.ReadFromJsonAsync<Starter.Contracts.Dashboard.DashboardResponse>(Cancellation);

        Assert.Equal(["Bob 1"], bobsList!.Items.Select(n => n.Title));
        Assert.Equal(1, bobsList.TotalCount);
        Assert.Equal(1, bobsDashboard!.ActiveNotes);
    }

    [Fact]
    public async Task Owner_sent_in_the_request_body_is_rejected()
    {
        var (_, alice) = await SignedInUserAsync();
        var (bob, _) = await SignedInUserAsync();

        using var response = await SendAsync(HttpMethod.Post, "/api/notes", alice.AccessToken,
            $$"""{"title":"Planted","content":"x","ownerId":"{{bob.Id}}"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = Fixture.Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Notes.AnyAsync(n => n.Title == "Planted", Cancellation));
    }

    [Fact]
    public async Task Created_note_is_owned_by_the_token_subject()
    {
        var (alice, tokens) = await SignedInUserAsync();

        var note = await CreateNoteAsync(tokens.AccessToken, "Mine");

        await using var scope = Fixture.Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Notes.SingleAsync(n => n.Id == note.Id, Cancellation);
        Assert.Equal(alice.Id, stored.OwnerId);
    }

    private async Task<NoteDetails> CreateNoteAsync(string token, string title)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/notes", token, new CreateNoteRequest { Title = title, Content = "content" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NoteDetails>(Cancellation))!;
    }
}
