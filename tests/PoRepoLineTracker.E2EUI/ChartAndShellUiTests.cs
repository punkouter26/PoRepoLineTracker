using FluentAssertions;
using Microsoft.Playwright;

namespace PoRepoLineTracker.E2EUI;

/// <summary>
/// Regression cover for the shell-layout and chart-rendering defects fixed in the mobile-first
/// pass. Every test here corresponds to a bug that shipped and that no existing test could catch,
/// because the suite only ever loaded /login as an anonymous visitor.
///
/// Each one asserts on the rendered result rather than on the CSS text: a rule that never matched
/// (the `::deep` shell selectors), a token that never resolved, or an axis that drew no labels all
/// look perfectly correct in the stylesheet and only fail in the DOM.
/// </summary>
[Collection(E2EUiCollection.Name)]
public sealed class ChartAndShellUiTests
{
    private readonly E2EUiFixture _fixture;

    public ChartAndShellUiTests(E2EUiFixture fixture) => _fixture = fixture;

    /// <summary>Waits for the repositories page to finish its initial data load.</summary>
    private static async Task WaitForRepositoriesAsync(IPage page)
    {
        await page.WaitForSelectorAsync(".rp-grid, .chart-card, .rp-onboarding-card",
            new PageWaitForSelectorOptions { Timeout = 25000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
            new PageWaitForLoadStateOptions { Timeout = 25000 });
    }

    /// <summary>
    /// Seeds synthetic history for the fake user, then waits for a chart to be painted.
    ///
    /// <para>Every chart assertion in this file used to open with
    /// <c>Skip.If(no chart rendered)</c> and skip on every run, because charts only render for a
    /// signed-in user with analysed repositories and the fake user had none — so the suite
    /// reported "45 passed, 12 skipped" while covering no chart at all. Seeding first turns those
    /// into real assertions.</para>
    ///
    /// <para>The remaining skip is honest: it fires only when no app is reachable or the host does
    /// not map the seed route (it is Development-only). A chart that fails to paint against seeded
    /// data is a FAILURE, waited for rather than skipped past — that is the whole point.</para>
    /// </summary>
    private static async Task SeedAndWaitForChartAsync(IPage page)
    {
        Skip.IfNot(await E2ESeeder.EnsureSeededAsync(),
            $"Could not seed chart data at {E2EUiFixture.BaseUrl} — is the app running in Development?");

        // Reload: the seed lands after the page's initial fetch, so the first render has nothing.
        await page.ReloadAsync(new PageReloadOptions { Timeout = 25000 });
        await WaitForRepositoriesAsync(page);

        await page.WaitForSelectorAsync(".rz-chart svg",
            new PageWaitForSelectorOptions { Timeout = 25000 });

        // A <svg> exists before its series are drawn. Waiting for a path is what makes the axis
        // and tick assertions below deterministic rather than a race against the render.
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.rz-chart svg path, .rz-chart svg text').length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 25000 });
    }

    // ─── Shell layout ────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task ShellGrid_PutsSidebarBesideBody_AndAdaptsToMobile()
    {
        var desktopPage = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories");
        await using var _ = desktopPage.Context;
        await WaitForRepositoriesAsync(desktopPage);

        var layout = await desktopPage.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.rz-layout')).display");
        var sidebarRight = await desktopPage.EvaluateAsync<int>(
            "() => Math.round(document.querySelector('.rz-sidebar').getBoundingClientRect().right)");
        var bodyLeftDesktop = await desktopPage.EvaluateAsync<int>(
            "() => Math.round(document.querySelector('.rz-body').getBoundingClientRect().left)");

        layout.Should().Be("grid");
        bodyLeftDesktop.Should().BeGreaterThanOrEqualTo(sidebarRight - 1,
            "the body must sit to the right of the sidebar, not underneath it");

        var mobilePage = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Mobile, "/repositories");
        await using var __ = mobilePage.Context;
        await WaitForRepositoriesAsync(mobilePage);

        var bodyLeftMobile = await mobilePage.EvaluateAsync<int>(
            "() => Math.round(document.querySelector('.rz-body').getBoundingClientRect().left)");
        bodyLeftMobile.Should().BeLessThanOrEqualTo(1,
            "the content column must start at the left edge once the sidebar becomes a drawer");
    }

    // ─── Theming ─────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Theme_DarkTheme_AppliesAttributeAndCharts()
    {
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        var theme = await page.EvaluateAsync<string?>(
            "() => document.documentElement.getAttribute('data-theme')");
        theme.Should().BeOneOf("light", "dark");

        var dark = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories", colorScheme: "dark");
        await using var __ = dark.Context;
        await SeedAndWaitForChartAsync(dark);

        var attribute = await dark.EvaluateAsync<string?>(
            "() => document.documentElement.getAttribute('data-theme')");
        attribute.Should().Be("dark");

        var background = await dark.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.rz-chart svg')).backgroundColor");
        background.Should().NotBe("rgb(255, 255, 255)");
    }

    // ─── Chart axes ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Chart_RendersTitledTickedAxes_AndStaysInsideCard()
    {
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories");
        await using var _ = page.Context;
        await SeedAndWaitForChartAsync(page);

        var text = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.rz-chart text')].map(t => t.textContent.trim())");

        text.Should().Contain("Date", "the category axis must be labelled");
        text.Should().Contain("Lines of Code", "the value axis must be labelled");

        var ticks = text.Where(t => !string.IsNullOrEmpty(t) && t != "Date" && t != "Lines of Code").ToArray();
        ticks.Should().HaveCountGreaterThan(2, "both axes must draw readable tick values");

        var labels = await page.EvaluateAsync<string[]>(
            @"() => [...document.querySelectorAll('.rz-chart .rz-category-axis text')]
                .map(t => t.textContent.trim())
                .filter(t => t && t !== 'Date')");

        labels.Should().NotBeEmpty("the category axis must draw date ticks for seeded history");
        labels.Should().OnlyHaveUniqueItems("every date tick must render a distinct label");

        var escaping = await page.EvaluateAsync<string[]>(
            @"() => {
                const svg = document.querySelector('.rz-chart svg');
                const card = svg?.closest('.rz-card');
                if (!card) return [];
                const c = card.getBoundingClientRect();
                return [...svg.querySelectorAll('text')]
                    .filter(t => (t.textContent || '').trim())
                    .filter(t => {
                        const r = t.getBoundingClientRect();
                        return r.bottom > c.bottom - 1 || r.right > c.right - 1 || r.left < c.left + 1;
                    })
                    .map(t => t.textContent.trim());
            }");

        escaping.Should().BeEmpty("every axis label and title must render inside its own card");
    }

    [SkippableFact]
    public async Task Chart_ResponsiveStepAndHeight()
    {
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories");
        await using var _ = page.Context;
        await SeedAndWaitForChartAsync(page);

        var oneYear = page.Locator(".chart-card").First.GetByText("1yr", new() { Exact = true });
        if (await oneYear.CountAsync() > 0)
        {
            await oneYear.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 25000 });

            var labelCount = await page.EvaluateAsync<int>(
                @"() => [...document.querySelectorAll('.rz-chart .rz-category-axis text')]
                    .filter(t => t.textContent.trim()).length");

            labelCount.Should().BeLessThan(40,
                "a year of daily points must be thinned to a readable number of tick labels");
        }

        var mobilePage = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Mobile, "/repositories");
        await using var __ = mobilePage.Context;
        await WaitForRepositoriesAsync(mobilePage);
        await SeedAndWaitForChartAsync(mobilePage);

        var height = await mobilePage.EvaluateAsync<double>(
            "() => document.querySelector('.rz-chart').getBoundingClientRect().height");

        height.Should().BeLessThan(400, "the chart must shrink on a phone");
        height.Should().BeGreaterThan(150, "but must stay tall enough to read");
    }

    // ─── Repository detail ───────────────────────────────────────────────────────

    private async Task<IPage> OpenFirstRepositoryAsync(ViewportSize viewport)
    {
        Skip.IfNot(await E2ESeeder.EnsureSeededAsync(),
            $"Could not seed chart data at {E2EUiFixture.BaseUrl} — is the app running in Development?");

        var page = await _fixture.OpenAuthenticatedAsync(viewport, "/repositories");
        await WaitForRepositoriesAsync(page);

        var row = page.Locator(".rz-data-row").First;
        await row.WaitForAsync(new LocatorWaitForOptions { Timeout = 25000 });

        await row.ClickAsync();
        await page.WaitForURLAsync("**/repositories/**", new PageWaitForURLOptions { Timeout = 20000 });
        await page.WaitForSelectorAsync(".chart-card", new PageWaitForSelectorOptions { Timeout = 25000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 25000 });
        return page;
    }

    [SkippableFact]
    public async Task Detail_RendersPanels_AndFitsMobileViewport()
    {
        var desktopPage = await OpenFirstRepositoryAsync(E2EUiFixture.Desktop);
        await using var _ = desktopPage.Context;

        var titles = await desktopPage.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.chart-card__title')].map(t => t.textContent.trim())");

        titles.Should().Contain("Line Count History");
        titles.Should().Contain("By Extension");
        titles.Should().Contain("Top Contributors");

        await desktopPage.WaitForSelectorAsync(".rz-chart svg", new PageWaitForSelectorOptions { Timeout = 25000 });

        var text = await desktopPage.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.rz-chart text')].map(t => t.textContent.trim())");

        text.Should().Contain("Date");
        text.Should().Contain("Lines of Code");

        var mobilePage = await OpenFirstRepositoryAsync(E2EUiFixture.Mobile);
        await using var __ = mobilePage.Context;

        var overflow = await mobilePage.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        overflow.Should().BeLessThanOrEqualTo(1,
            "the contributor grid and chart panels must fit the phone viewport");
    }
}
