[assembly: AssemblyFixture(typeof(Starter.E2E.Tests.Infrastructure.PlaywrightFixture))]

namespace Starter.E2E.Tests.Infrastructure;

/// <summary>One Playwright driver and one browser process for the whole run; every test gets its own isolated context.</summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright? playwright;

    public IBrowser Browser { get; private set; } = default!;
    public E2ESettings Settings { get; } = E2ESettings.Current;
    public Mailbox Mailbox { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        playwright = await Playwright.CreateAsync();
        var browserType = Settings.Browser switch
        {
            "firefox" => playwright.Firefox,
            "webkit" => playwright.Webkit,
            _ => playwright.Chromium,
        };

        Browser = await browserType.LaunchAsync(new()
        {
            Headless = Settings.Headless,
            ExecutablePath = Settings.BrowserExecutable,
        });
        Mailbox = new Mailbox(Settings.MailpitUrl);
    }

    public async ValueTask DisposeAsync()
    {
        if (Mailbox is not null)
        {
            await TestDataCleanup.RunAsync(Settings, Mailbox);
            Mailbox.Dispose();
        }

        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        playwright?.Dispose();
    }
}
