using FluentAssertions;
using Microsoft.Playwright;

namespace PoRepoLineTracker.E2EUI;

/// <summary>
/// Rule 4.5 — WCAG 2.2 AA on interactive elements. These are the machine-checkable subset
/// (accessible names, document language, focus order, contrast-independent structure); they do
/// not replace a manual audit, but they catch the regressions that reach production.
/// </summary>
[Collection(E2EUiCollection.Name)]
public sealed class AccessibilityUiTests
{
    private readonly E2EUiFixture _fixture;

    public AccessibilityUiTests(E2EUiFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Document_DeclaresItsLanguage()
    {
        // WCAG 3.1.1 — screen readers pick pronunciation from this.
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;

        (await page.GetAttributeAsync("html", "lang")).Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task Document_HasANonEmptyTitle()
    {
        // WCAG 2.4.2 — the title is how a user distinguishes tabs.
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;

        (await page.TitleAsync()).Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task EveryButton_HasAnAccessibleName()
    {
        // WCAG 4.1.2 — an icon-only button with no aria-label announces as "button".
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        await page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 20000 });

        var unnamed = await page.EvaluateAsync<int>(@"() =>
            [...document.querySelectorAll('button')]
                .filter(el => el.offsetParent !== null)
                .filter(el => !(el.innerText || '').trim()
                           && !el.getAttribute('aria-label')
                           && !el.getAttribute('aria-labelledby')
                           && !el.getAttribute('title'))
                .length");

        unnamed.Should().Be(0, "every visible button must announce a name");
    }

    [SkippableFact]
    public async Task EveryLink_HasAnAccessibleName()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        await page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 20000 });

        var unnamed = await page.EvaluateAsync<int>(@"() =>
            [...document.querySelectorAll('a[href]')]
                .filter(el => el.offsetParent !== null)
                .filter(el => !(el.innerText || '').trim()
                           && !el.getAttribute('aria-label')
                           && !el.getAttribute('title'))
                .length");

        unnamed.Should().Be(0);
    }

    [SkippableFact]
    public async Task EveryImage_HasAltText()
    {
        // WCAG 1.1.1 — decorative images must still carry an explicit empty alt.
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        await page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 20000 });

        var missingAlt = await page.EvaluateAsync<int>(
            "() => [...document.querySelectorAll('img')].filter(el => el.getAttribute('alt') === null).length");

        missingAlt.Should().Be(0);
    }

    [SkippableFact]
    public async Task InteractiveElements_AreKeyboardReachable()
    {
        // WCAG 2.1.1 — pressing Tab must move focus onto a real control.
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        await page.WaitForSelectorAsync("button, a", new PageWaitForSelectorOptions { Timeout = 20000 });

        await page.Keyboard.PressAsync("Tab");
        var focused = await page.EvaluateAsync<string>("() => document.activeElement?.tagName ?? ''");

        focused.Should().NotBe("BODY", "focus must land on an interactive element");
    }

    [SkippableFact]
    public async Task NoElement_OptsOutOfTheTabOrder()
    {
        // A positive tabindex reorders the tab sequence away from DOM order (WCAG 2.4.3).
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        await page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 20000 });

        var positiveTabIndex = await page.EvaluateAsync<int>(
            "() => [...document.querySelectorAll('[tabindex]')].filter(el => Number(el.getAttribute('tabindex')) > 0).length");

        positiveTabIndex.Should().Be(0);
    }

    [SkippableFact]
    public async Task EveryFormInput_IsLabelled()
    {
        // WCAG 1.3.1 / 3.3.2 — an unlabelled field announces only its type.
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        await page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 20000 });

        var unlabelled = await page.EvaluateAsync<int>(@"() =>
            [...document.querySelectorAll('input:not([type=hidden]), select, textarea')]
                .filter(el => el.offsetParent !== null)
                .filter(el => !el.getAttribute('aria-label')
                           && !el.getAttribute('aria-labelledby')
                           && !el.getAttribute('placeholder')
                           && !(el.id && document.querySelector(`label[for='${el.id}']`))
                           && !el.closest('label'))
                .length");

        unlabelled.Should().Be(0);
    }

    [SkippableFact]
    public async Task Mobile_KeepsAccessibleNames()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Mobile, "/login");
        await using var _ = page.Context;
        await page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 20000 });

        var unnamed = await page.EvaluateAsync<int>(@"() =>
            [...document.querySelectorAll('button')]
                .filter(el => el.offsetParent !== null)
                .filter(el => !(el.innerText || '').trim()
                           && !el.getAttribute('aria-label')
                           && !el.getAttribute('title'))
                .length");

        unnamed.Should().Be(0, "the mobile layout must not drop labels the desktop layout has");
    }
}
