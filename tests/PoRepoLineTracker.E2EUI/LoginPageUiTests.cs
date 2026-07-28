using FluentAssertions;
using Microsoft.Playwright;

namespace PoRepoLineTracker.E2EUI;

/// <summary>
/// Rule 2.2 — C# Playwright UI E2E (replaces the legacy TypeScript suite). Covers the only
/// screen an unauthenticated visitor can reach, which is also the screen the deploy smoke test
/// lands on (Rule 6.3).
/// </summary>
[Collection(E2EUiCollection.Name)]
public sealed class LoginPageUiTests
{
    private readonly E2EUiFixture _fixture;

    public LoginPageUiTests(E2EUiFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Unauthenticated_Visit_Redirects_To_Login()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop);
        await using var _ = page.Context;

        await page.WaitForURLAsync("**/login", new PageWaitForURLOptions { Timeout = 20000 });

        page.Url.Should().Contain("/login");
    }

    [SkippableFact]
    public async Task BlazorShell_Boots()
    {
        // Rule 6.3 asks the smoke test to confirm the Blazor render tree initialises. If the WASM
        // runtime failed to start, <app> stays at its loading placeholder and nothing renders.
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;

        await page.WaitForSelectorAsync("button, a", new PageWaitForSelectorOptions { Timeout = 20000 });

        var rendered = await page.Locator("button, a").CountAsync();
        rendered.Should().BeGreaterThan(0, "an initialised render tree produces interactive elements");
    }

    [SkippableFact]
    public async Task LoginPage_HasNoUnhandledConsoleErrors()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;

        var errors = new List<string>();
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error") errors.Add(msg.Text);
        };

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20000 });

        errors.Should().BeEmpty("a clean boot logs no console errors");
    }

    [SkippableFact]
    public async Task LoginPage_OffersASignInAction()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;

        await page.WaitForSelectorAsync("button, a", new PageWaitForSelectorOptions { Timeout = 20000 });

        var signIn = page.GetByText("Sign in", new PageGetByTextOptions { Exact = false });
        (await signIn.CountAsync()).Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Page_HasATitle()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;

        (await page.TitleAsync()).Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task ProtectedRoute_Anonymous_DoesNotRenderRepositoryData()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/repositories");
        await using var _ = page.Context;

        await page.WaitForURLAsync("**/login", new PageWaitForURLOptions { Timeout = 20000 });

        page.Url.Should().Contain("/login", "an anonymous visitor must never reach the data screens");
    }

    [SkippableFact]
    public async Task UnknownRoute_ServesTheShellNotAServerError()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/definitely-not-a-page");
        await using var _ = page.Context;

        var body = await page.InnerTextAsync("body", new PageInnerTextOptions { Timeout = 20000 });

        body.Should().NotContain("500");
    }
}
