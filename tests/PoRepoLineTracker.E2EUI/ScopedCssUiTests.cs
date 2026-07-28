using FluentAssertions;
using Microsoft.Playwright;

namespace PoRepoLineTracker.E2EUI;

/// <summary>
/// Rule 4.3 — inline styles are forbidden, so the rules that used to sit on <c>style=</c> must now
/// arrive from a scoped <c>.razor.css</c>. Asserting on the *computed* style is what makes the
/// extraction verifiable: a class that never matched (a missing <c>::deep</c>, a wrong anchor)
/// silently drops the styling, and only the computed value catches it.
/// </summary>
[Collection(E2EUiCollection.Name)]
public sealed class ScopedCssUiTests
{
    private readonly E2EUiFixture _fixture;

    public ScopedCssUiTests(E2EUiFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task LoginCard_GetsItsWidthFromScopedCss()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        await page.WaitForSelectorAsync(".login-card", new PageWaitForSelectorOptions { Timeout = 20000 });

        var maxWidth = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.login-card')).maxWidth");

        maxWidth.Should().Be("420px", "the extracted rule must still reach the Radzen-rendered card");
    }

    [SkippableFact]
    public async Task LoginCta_GetsItsWidthFromScopedCss()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        await page.WaitForSelectorAsync(".login-cta-btn", new PageWaitForSelectorOptions { Timeout = 20000 });

        var fontWeight = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.login-cta-btn')).fontWeight");

        fontWeight.Should().Be("600");
    }

    [SkippableFact]
    public async Task LoginDivider_GetsItsLayoutFromScopedCss()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        // Attached, not Visible: an <hr> with border-only styling has zero height, which
        // Playwright reports as not visible.
        await page.WaitForSelectorAsync(".login-divider",
            new PageWaitForSelectorOptions { Timeout = 20000, State = WaitForSelectorState.Attached });

        // Asserting margin rather than the border: the extracted rule keeps the original
        // reference to --rz-border-disabled-color, which the active Radzen theme does not define,
        // so the border has never actually rendered. See the note in Login.razor.css.
        var margin = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.login-divider')).marginTop");

        margin.Should().Be("0px");
    }

    [SkippableFact]
    public async Task Logomark_GlyphGetsItsSizeFromScopedCss()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        await page.WaitForSelectorAsync(".login-logomark-icon__glyph", new PageWaitForSelectorOptions { Timeout = 20000 });

        var fontSize = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.login-logomark-icon__glyph')).fontSize");

        // 1.6rem against the 16px root.
        fontSize.Should().Be("25.6px");
    }

    [SkippableFact]
    public async Task NoElementOnTheLoginScreen_CarriesAnInlineStyle()
    {
        // The rule itself, asserted against the rendered DOM. Radzen sets inline styles of its
        // own on the elements it generates (`rz-*` classes, and `rzi` on icons), so this counts
        // only the elements our own markup owns.
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        await page.WaitForSelectorAsync(".login-card", new PageWaitForSelectorOptions { Timeout = 20000 });

        var inlineStyled = await page.EvaluateAsync<string[]>(@"() =>
            [...document.querySelectorAll('.login-canvas [style]')]
                .filter(el => ![...el.classList].some(c => c.startsWith('rz-') || c === 'rzi'))
                .map(el => el.tagName + '.' + el.className)");

        inlineStyled.Should().BeEmpty("every style on our own markup must come from Login.razor.css");
    }
}
