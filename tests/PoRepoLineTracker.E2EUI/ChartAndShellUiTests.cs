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

    // ─── Shell layout ────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Mobile_SidebarDoesNotReserveADeadGutter()
    {
        // The bug: .rz-layout kept `grid-template-columns: 220px 1fr` at every width and the
        // mobile rule only did `transform: translateX(-100%)` on the sidebar. The track stayed,
        // so a 390px phone lost 220px to an empty column and squeezed the body into the rest.
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Mobile, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        var body = await page.EvaluateAsync<int>(
            "() => Math.round(document.querySelector('.rz-body').getBoundingClientRect().left)");

        body.Should().BeLessThanOrEqualTo(1,
            "the content column must start at the left edge once the sidebar becomes a drawer");
    }

    [SkippableFact]
    public async Task Mobile_ContentUsesTheFullViewportWidth()
    {
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Mobile, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        var bodyWidth = await page.EvaluateAsync<int>(
            "() => Math.round(document.querySelector('.rz-body').getBoundingClientRect().width)");
        var viewport = await page.EvaluateAsync<int>("() => window.innerWidth");

        // Allow a scrollbar's worth of slack; the old layout was ~220px short.
        bodyWidth.Should().BeGreaterThan(viewport - 20,
            "the body must span the viewport, not the viewport minus a phantom sidebar track");
    }

    [SkippableFact]
    public async Task Mobile_RepositoriesPageDoesNotScrollHorizontally()
    {
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Mobile, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        overflow.Should().BeLessThanOrEqualTo(1, "the data grid must not push the page sideways");
    }

    [SkippableFact]
    public async Task Desktop_ShellGridPutsTheSidebarBesideTheBody()
    {
        // The shell grid used to be declared only in MainLayout.razor.css behind `::deep`, which
        // compiles to `[b-scope] .rz-layout` and matched nothing — MainLayout roots at a
        // component, so no element carried the scope id. It survived only because the rules had
        // been copy-pasted into app.css.
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        var layout = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.rz-layout')).display");
        var sidebarRight = await page.EvaluateAsync<int>(
            "() => Math.round(document.querySelector('.rz-sidebar').getBoundingClientRect().right)");
        var bodyLeft = await page.EvaluateAsync<int>(
            "() => Math.round(document.querySelector('.rz-body').getBoundingClientRect().left)");

        layout.Should().Be("grid");
        bodyLeft.Should().BeGreaterThanOrEqualTo(sidebarRight - 1,
            "the body must sit to the right of the sidebar, not underneath it");
    }

    [SkippableFact]
    public async Task Mobile_HeaderControlsStayInsideTheHeader()
    {
        // The header row is a fixed height. When the three header regions were all assigned to
        // the same grid column they auto-placed into separate rows, and the drawer toggle spilled
        // out below the coloured bar onto the page background.
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Mobile, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        var escaping = await page.EvaluateAsync<string[]>(
            @"() => {
                const header = document.querySelector('.rz-header');
                const h = header.getBoundingClientRect();
                return [...header.querySelectorAll('button, a')]
                    .filter(el => el.offsetParent !== null)
                    .filter(el => {
                        const r = el.getBoundingClientRect();
                        return r.height > 0 && (r.bottom > h.bottom + 1 || r.top < h.top - 1);
                    })
                    .map(el => el.tagName + '.' + (el.className || '(none)'));
            }");

        escaping.Should().BeEmpty("every header control must render inside the header bar");
    }

    // ─── Theming ─────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Theme_DataThemeAttributeIsAlwaysSet()
    {
        // The bug: index.html set data-theme only when the user actively toggled, but every dark
        // rule in app.css keyed off prefers-color-scheme. The two never agreed.
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        var theme = await page.EvaluateAsync<string?>(
            "() => document.documentElement.getAttribute('data-theme')");

        theme.Should().BeOneOf("light", "dark");
    }

    [SkippableFact]
    public async Task Theme_FollowsTheOsPreferenceOnFirstVisit()
    {
        var dark = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories", colorScheme: "dark");
        await using var _ = dark.Context;
        await WaitForRepositoriesAsync(dark);

        var attribute = await dark.EvaluateAsync<string?>(
            "() => document.documentElement.getAttribute('data-theme')");

        attribute.Should().Be("dark",
            "with no saved choice the boot script must resolve the theme from the OS preference");
    }

    [SkippableFact]
    public async Task Charts_AreNotPaintedWhiteUnderTheDarkTheme()
    {
        // The bug: `.rz-chart svg { background-color: white !important }` with the dark override
        // gated behind prefers-color-scheme, so the toggle produced a white chart in a dark card.
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories", colorScheme: "dark");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        Skip.If(await page.Locator(".rz-chart svg").CountAsync() == 0, "no chart rendered — seed data required.");

        var background = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.rz-chart svg')).backgroundColor");

        background.Should().NotBe("rgb(255, 255, 255)",
            "the chart surface must follow the theme rather than being forced opaque white");
    }

    // ─── Chart axes ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Chart_RendersBothAxisTitles()
    {
        // No chart in the app declared <RadzenAxisTitle>, even though app.css styled
        // `.rz-axis-title` — an element that never existed in the DOM.
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        Skip.If(await page.Locator(".rz-chart svg").CountAsync() == 0, "no chart rendered — seed data required.");

        var text = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.rz-chart text')].map(t => t.textContent.trim())");

        text.Should().Contain("Date", "the category axis must be labelled");
        text.Should().Contain("Lines of Code", "the value axis must be labelled");
    }

    [SkippableFact]
    public async Task Chart_RendersTickLabelsOnBothAxes()
    {
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        Skip.If(await page.Locator(".rz-chart svg").CountAsync() == 0, "no chart rendered — seed data required.");

        // Tick labels are every non-empty text node that is not one of the two axis titles.
        var ticks = await page.EvaluateAsync<string[]>(
            @"() => [...document.querySelectorAll('.rz-chart text')]
                .map(t => t.textContent.trim())
                .filter(t => t && t !== 'Date' && t !== 'Lines of Code')");

        ticks.Should().HaveCountGreaterThan(2, "both axes must draw readable tick values");
    }

    [SkippableFact]
    public async Task Chart_CategoryLabelsAreAllDistinct()
    {
        // The bug this pins down: with only a few days of history in a wide card, the axis placed
        // ticks less than a day apart, and the "MMM d" format rendered them as repeats —
        // "Jul 21, Jul 21, Jul 22, Jul 22". Neither the default spacing nor a pixel-based
        // TickDistance avoids it; the step has to be quantised to whole days.
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        Skip.If(await page.Locator(".rz-chart svg").CountAsync() == 0, "no chart rendered — seed data required.");

        var labels = await page.EvaluateAsync<string[]>(
            @"() => [...document.querySelectorAll('.rz-chart .rz-category-axis text')]
                .map(t => t.textContent.trim())
                .filter(t => t && t !== 'Date')");

        Skip.If(labels.Length == 0, "category axis drew no labels — seed data required.");

        labels.Should().OnlyHaveUniqueItems("every date tick must render a distinct label");
    }

    [SkippableFact]
    public async Task Chart_DoesNotCrowdTheCategoryAxisOverTheFullYear()
    {
        // The bug: 365 daily categories with no Step drew one label per day, collapsing the axis
        // into an unreadable smear. ChartFormat.CategoryStepDays thins them per range.
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        Skip.If(await page.Locator(".rz-chart svg").CountAsync() == 0, "no chart rendered — seed data required.");

        // Select the 1yr range on the growth-overview card.
        var oneYear = page.Locator(".chart-card").First.GetByText("1yr", new() { Exact = true });
        Skip.If(await oneYear.CountAsync() == 0, "range selector not present.");
        await oneYear.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 25000 });

        var labelCount = await page.EvaluateAsync<int>(
            @"() => [...document.querySelectorAll('.rz-chart .rz-category-axis text')]
                .filter(t => t.textContent.trim()).length");

        labelCount.Should().BeLessThan(40,
            "a year of daily points must be thinned to a readable number of tick labels");
    }

    [SkippableFact]
    public async Task Chart_TextStaysInsideItsCard()
    {
        // Asserts on the text nodes, not the svg box. Radzen places the category axis title just
        // *below* the svg, so measuring only the svg proves nothing: `overflow: hidden` keeps the
        // svg inside the card while silently clipping the "Date" label out of existence, and
        // `overflow: visible` with no reserved padding lets it paint onto the next card. Both
        // failure modes are visible only from the label's own rect.
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        Skip.If(await page.Locator(".rz-chart svg").CountAsync() == 0, "no chart rendered — seed data required.");

        var escaping = await page.EvaluateAsync<string[]>(
            @"() => {
                const svg = document.querySelector('.rz-chart svg');
                const card = svg.closest('.rz-card');
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

    // ─── Repository detail ───────────────────────────────────────────────────────

    /// <summary>Opens the first repository from the grid, or skips when the account has none.</summary>
    private async Task<IPage> OpenFirstRepositoryAsync(ViewportSize viewport)
    {
        var page = await _fixture.OpenAuthenticatedAsync(viewport, "/repositories");
        await WaitForRepositoriesAsync(page);

        var row = page.Locator(".rz-data-row").First;
        if (await row.CountAsync() == 0)
        {
            await page.Context.DisposeAsync();
            throw new SkipException("no repositories for this user — seed data required.");
        }

        await row.ClickAsync();
        await page.WaitForURLAsync("**/repositories/**", new PageWaitForURLOptions { Timeout = 20000 });
        await page.WaitForSelectorAsync(".chart-card", new PageWaitForSelectorOptions { Timeout = 25000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 25000 });
        return page;
    }

    [SkippableFact]
    public async Task Detail_RendersAllThreeChartPanels()
    {
        var page = await OpenFirstRepositoryAsync(E2EUiFixture.Desktop);
        await using var _ = page.Context;

        var titles = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.chart-card__title')].map(t => t.textContent.trim())");

        titles.Should().Contain("Line Count History");
        titles.Should().Contain("By Extension");
        titles.Should().Contain("Largest Files");
        titles.Should().Contain("Top Contributors");
    }

    [SkippableFact]
    public async Task Detail_LineHistoryChartLabelsBothAxes()
    {
        var page = await OpenFirstRepositoryAsync(E2EUiFixture.Desktop);
        await using var _ = page.Context;

        Skip.If(await page.Locator(".rz-chart svg").CountAsync() == 0, "no chart rendered.");

        var text = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.rz-chart text')].map(t => t.textContent.trim())");

        text.Should().Contain("Date");
        text.Should().Contain("Lines of Code");
    }

    [SkippableFact]
    public async Task Detail_ExtensionBreakdownRendersADonut()
    {
        // The extension panel was a stack of bare <progress> bars; a donut states the
        // part-of-whole relationship the data actually describes.
        var page = await OpenFirstRepositoryAsync(E2EUiFixture.Desktop);
        await using var _ = page.Context;

        var hasDonut = await page.EvaluateAsync<bool>(
            @"() => [...document.querySelectorAll('.chart-card')]
                .some(c => c.querySelector('.chart-card__title')?.textContent.trim() === 'By Extension'
                        && c.querySelector('.rz-chart svg path'))");

        Skip.If(!hasDonut && await page.Locator(".chart-card__empty").CountAsync() > 0,
            "no extension data for this repository.");

        hasDonut.Should().BeTrue("the extension breakdown must draw its donut series");
    }

    [SkippableFact]
    public async Task Detail_MobileDoesNotScrollHorizontally()
    {
        var page = await OpenFirstRepositoryAsync(E2EUiFixture.Mobile);
        await using var _ = page.Context;

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        overflow.Should().BeLessThanOrEqualTo(1,
            "the contributor grid and chart panels must fit the phone viewport");
    }

    [SkippableFact]
    public async Task Detail_MobileTapTargetsAreLargeEnough()
    {
        var page = await OpenFirstRepositoryAsync(E2EUiFixture.Mobile);
        await using var _ = page.Context;

        var undersized = await page.EvaluateAsync<string[]>(
            @"() => [...document.querySelectorAll('button, a')]
                .filter(el => el.offsetParent !== null)
                .filter(el => {
                    const r = el.getBoundingClientRect();
                    return r.width > 0 && r.height > 0 && (r.width < 24 || r.height < 24);
                })
                .map(el => el.tagName + '.' + (el.className || '(none)'))");

        undersized.Should().BeEmpty("every visible control must meet the 24x24 minimum target size");
    }

    // ─── Mobile chart sizing ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Mobile_ChartHeightIsReducedNotPinnedAt400px()
    {
        // `min-height: 400px` on .rz-chart outranked the mobile `height: 240px`, so phones got a
        // full-height chart regardless. One clamp() replaces all three declarations.
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Mobile, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        Skip.If(await page.Locator(".rz-chart").CountAsync() == 0, "no chart rendered — seed data required.");

        var height = await page.EvaluateAsync<double>(
            "() => document.querySelector('.rz-chart').getBoundingClientRect().height");

        height.Should().BeLessThan(400, "the chart must shrink on a phone");
        height.Should().BeGreaterThan(150, "but must stay tall enough to read");
    }

    [SkippableFact]
    public async Task Mobile_TapTargetsOnTheRepositoriesPageAreLargeEnough()
    {
        // WCAG 2.2 AA, 2.5.8 Target Size (Minimum) — 24x24 CSS pixels.
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Mobile, "/repositories");
        await using var _ = page.Context;
        await WaitForRepositoriesAsync(page);

        var undersized = await page.EvaluateAsync<string[]>(
            @"() => [...document.querySelectorAll('button, a')]
                .filter(el => el.offsetParent !== null)
                .filter(el => {
                    const r = el.getBoundingClientRect();
                    return r.width > 0 && r.height > 0 && (r.width < 24 || r.height < 24);
                })
                .map(el => el.tagName + '.' + (el.className || '(none)'))");

        undersized.Should().BeEmpty("every visible control must meet the 24x24 minimum target size");
    }
}
