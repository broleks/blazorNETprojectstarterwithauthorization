using System.Text.RegularExpressions;
using Starter.E2E.Tests.Infrastructure;

namespace Starter.E2E.Tests.Tests;

public sealed class NotesTests(PlaywrightFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Note_can_be_created_read_edited_and_deleted()
    {
        await SignedInAccountAsync(Page);
        await Page.GotoAsync("/notes");
        await Expect(Page.GetByTestId("empty-notes")).ToBeVisibleAsync();

        await CreateNoteAsync(Page, "Shopping list", "Milk and bread");
        await Page.GotoAsync("/notes");
        await Expect(Page.GetByTestId("note-item")).ToHaveCountAsync(1);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Shopping list" }).ClickAsync();
        await Expect(Page.GetByLabel("Content")).ToHaveValueAsync("Milk and bread");

        await Page.GetByLabel("Title").FillAsync("Weekend shopping");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByTestId("toast")).ToContainTextAsync("Note saved.");

        // Cancelling the confirmation keeps the note.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Delete note?" });
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(Page.GetByLabel("Title")).ToHaveValueAsync("Weekend shopping");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Delete permanently" }).ClickAsync();
        await Expect(Page.GetByTestId("toast")).ToContainTextAsync("Note deleted.");
        await Expect(Page).ToHaveURLAsync(new Regex("/notes$"));
        await Expect(Page.GetByTestId("empty-notes")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Note_can_be_archived_and_restored()
    {
        await SignedInAccountAsync(Page);
        await CreateNoteAsync(Page, "Old idea");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Archive" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/archive$"));
        await Expect(Page.GetByTestId("toast")).ToContainTextAsync("Note archived.");
        var archived = Page.GetByTestId("note-item").Filter(new() { HasText = "Old idea" });
        await Expect(archived).ToBeVisibleAsync();

        await archived.GetByRole(AriaRole.Button, new() { Name = "Restore Old idea" }).ClickAsync();
        await Expect(Page.GetByTestId("toast")).ToContainTextAsync("was restored");
        await Expect(Page.GetByTestId("empty-notes")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "My Notes" }).ClickAsync();
        await Expect(Page.GetByTestId("note-item").Filter(new() { HasText = "Old idea" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Dashboard_reflects_the_users_real_data()
    {
        await SignedInAccountAsync(Page);
        await Expect(Page.GetByTestId("stat-active")).ToHaveTextAsync("0");
        await Expect(Page.GetByTestId("empty-recent")).ToBeVisibleAsync();

        await CreateNoteAsync(Page, "First");
        await CreateNoteAsync(Page, "Second");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Archive" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/archive$"));
        await Page.GetByRole(AriaRole.Link, new() { Name = "Dashboard" }).ClickAsync();

        await Expect(Page.GetByTestId("stat-active")).ToHaveTextAsync("1");
        await Expect(Page.GetByTestId("stat-archived")).ToHaveTextAsync("1");
        await Expect(Page.GetByTestId("note-item")).ToHaveTextAsync(new Regex("First"));
    }

    [Fact]
    public async Task Users_cannot_see_each_others_notes()
    {
        await SignedInAccountAsync(Page, "Alice");
        await CreateNoteAsync(Page, "Alice's private note");
        var aliceNoteUrl = Page.Url;

        var bob = await NewPageAsync();
        await SignedInAccountAsync(bob, "Bob");
        await bob.GotoAsync("/notes");
        await Expect(bob.GetByTestId("empty-notes")).ToBeVisibleAsync();

        // Even with the exact address of Alice's note, Bob gets "not found".
        await bob.GotoAsync(aliceNoteUrl);
        await Expect(bob.GetByTestId("api-error")).ToContainTextAsync("We could not find");
        await Expect(bob.GetByText("Alice's private note")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Api_errors_and_invalid_input_are_shown_as_friendly_messages()
    {
        await SignedInAccountAsync(Page);

        // A note that does not exist: the API answers 404, the UI explains it without technical details.
        await Page.GotoAsync($"/notes/{Guid.NewGuid()}");
        var error = Page.GetByTestId("api-error");
        await Expect(error).ToContainTextAsync("We could not find what you were looking for");
        await Expect(error).Not.ToContainTextAsync("404");
        await Expect(Page.GetByText("Exception")).ToHaveCountAsync(0);

        // Client-side validation (same rules as the API).
        await Page.GotoAsync("/notes/new");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByText("Enter a title.")).ToBeVisibleAsync();
    }
}
