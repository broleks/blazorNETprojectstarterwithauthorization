using System.Text.RegularExpressions;
using Starter.E2E.Tests.Infrastructure;

namespace Starter.E2E.Tests.Tests;

public sealed class MobileTests(PlaywrightFixture fixture) : E2ETestBase(fixture)
{
    // A typical phone screen.
    protected override ViewportSize Viewport => new() { Width = 390, Height = 844 };

    [Fact]
    public async Task Navigation_works_through_the_menu_on_a_small_screen()
    {
        await SignedInAccountAsync(Page);
        var menuButton = Page.GetByTestId("menu-toggle");
        var archiveLink = Page.GetByRole(AriaRole.Link, new() { Name = "Archive", Exact = true });

        await Expect(menuButton).ToBeVisibleAsync();
        await Expect(menuButton).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(archiveLink).ToBeHiddenAsync();

        await menuButton.ClickAsync();
        await Expect(menuButton).ToHaveAttributeAsync("aria-expanded", "true");
        await archiveLink.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/archive$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Archive", Level = 1 })).ToBeVisibleAsync();
        await Expect(archiveLink).ToBeHiddenAsync();

        // The main task still works on a phone.
        await CreateNoteAsync(Page, "Written on a phone");
        await Expect(Page.GetByLabel("Title")).ToHaveValueAsync("Written on a phone");
    }
}
