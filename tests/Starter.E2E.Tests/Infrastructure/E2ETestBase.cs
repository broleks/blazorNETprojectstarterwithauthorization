using System.Text.RegularExpressions;

namespace Starter.E2E.Tests.Infrastructure;

/// <summary>
/// Base class for end-to-end tests. Each test gets a fresh browser context (own cookies and storage, nothing
/// shared with other tests) with tracing on. When a test fails, a screenshot and a Playwright trace are saved to
/// the artifacts folder; open the trace with "pwsh bin/Debug/net10.0/playwright.ps1 show-trace &lt;file&gt;" or
/// https://trace.playwright.dev. Traces contain everything typed into the page, so treat them as sensitive.
/// </summary>
public abstract class E2ETestBase(PlaywrightFixture fixture) : IAsyncLifetime
{
    private readonly List<IBrowserContext> contexts = [];

    protected PlaywrightFixture Fixture { get; } = fixture;
    protected E2ESettings Settings => Fixture.Settings;
    protected Mailbox Mailbox => Fixture.Mailbox;
    protected IPage Page { get; private set; } = default!;

    /// <summary>Viewport of the main page. Override for mobile tests.</summary>
    protected virtual ViewportSize Viewport => new() { Width = 1280, Height = 800 };

    public async ValueTask InitializeAsync()
    {
        Page = await NewPageAsync();
    }

    /// <summary>A page in a new, isolated browser context (another user, another device).</summary>
    protected async Task<IPage> NewPageAsync(ViewportSize? viewport = null)
    {
        var context = await Fixture.Browser.NewContextAsync(new()
        {
            BaseURL = Settings.WebUrl.ToString(),
            IgnoreHTTPSErrors = Settings.IgnoreHttpsErrors,
            ViewportSize = viewport ?? Viewport,
            Locale = "en-US",
        });
        context.SetDefaultTimeout(15_000);
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Title = TestName });
        contexts.Add(context);
        return await context.NewPageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        var failed = TestContext.Current.TestState?.Result == TestResult.Failed;
        for (var i = 0; i < contexts.Count; i++)
        {
            var context = contexts[i];
            if (failed)
            {
                var folder = Directory.CreateDirectory(Path.Combine(Settings.ArtifactsFolder, SafeName(TestName))).FullName;
                foreach (var page in context.Pages)
                {
                    await page.ScreenshotAsync(new() { Path = Path.Combine(folder, $"context{i}-screenshot.png"), FullPage = true });
                }

                await context.Tracing.StopAsync(new() { Path = Path.Combine(folder, $"context{i}-trace.zip") });
            }
            else
            {
                await context.Tracing.StopAsync();
            }

            await context.CloseAsync();
        }

        GC.SuppressFinalize(this);
    }

    private static string TestName => TestContext.Current.Test?.TestDisplayName ?? "unknown-test";

    private static string SafeName(string name) => Regex.Replace(name, "[^A-Za-z0-9_.-]+", "_");

    // ---- Account helpers -------------------------------------------------------------------------------------

    protected static TestAccount NewAccount(string displayName = "E2E Tester") =>
        new($"e2e-{Guid.NewGuid():N}@{E2ESettings.EmailDomain}", "e2e password that is long", displayName);

    /// <summary>Starts at the web app and fills in the registration form on the identity server.</summary>
    protected static async Task RegisterAsync(IPage page, TestAccount account)
    {
        await page.GotoAsync("/");
        await page.GetByRole(AriaRole.Link, new() { Name = "Create an account" }).ClickAsync();
        await page.GetByLabel("Display name").FillAsync(account.DisplayName);
        await page.GetByLabel("Email").FillAsync(account.Email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(account.Password);
        await page.GetByLabel("Confirm password").FillAsync(account.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Check your email" })).ToBeVisibleAsync();
    }

    /// <summary>Opens the confirmation link from the test inbox and continues to the sign-in page.</summary>
    protected async Task ConfirmEmailAsync(IPage page, TestAccount account)
    {
        var link = await Mailbox.WaitForLinkAsync(account.Email, "Confirm your email address");
        await page.GotoAsync(link);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Email confirmed" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Continue" }).ClickAsync();
    }

    /// <summary>Fills in the sign-in form (the page must already show it).</summary>
    protected static async Task SubmitSignInAsync(IPage page, string email, string password)
    {
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
    }

    /// <summary>Opens the app, signs in and waits for the dashboard.</summary>
    protected static async Task SignInAsync(IPage page, TestAccount account)
    {
        await page.GotoAsync("/");
        await SubmitSignInAsync(page, account.Email, account.Password);
        await ExpectDashboardAsync(page);
    }

    /// <summary>Registers, confirms and signs in a new account; returns with the dashboard open.</summary>
    protected async Task<TestAccount> SignedInAccountAsync(IPage page, string displayName = "E2E Tester")
    {
        var account = NewAccount(displayName);
        await RegisterAsync(page, account);
        await ConfirmEmailAsync(page, account);
        await SubmitSignInAsync(page, account.Email, account.Password);
        await ExpectDashboardAsync(page);
        return account;
    }

    protected static async Task ExpectDashboardAsync(IPage page) =>
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Level = 1 })).ToBeVisibleAsync();

    protected static async Task CreateNoteAsync(IPage page, string title, string content = "")
    {
        await page.GotoAsync("/notes/new");
        await page.GetByLabel("Title").FillAsync(title);
        await page.GetByLabel("Content").FillAsync(content);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(page.GetByTestId("toast")).ToContainTextAsync("Note created.");
        await Expect(page).ToHaveURLAsync(new Regex("/notes/[0-9a-f-]{36}$"));
    }
}

public sealed record TestAccount(string Email, string Password, string DisplayName);
