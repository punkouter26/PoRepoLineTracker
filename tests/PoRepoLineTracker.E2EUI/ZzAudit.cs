using System.Text.Json;
using Microsoft.Playwright;

namespace PoRepoLineTracker.E2EUI;

// TEMPORARY runtime probe — deleted after use.
[Collection(E2EUiCollection.Name)]
public sealed class ZzAudit
{
    private readonly E2EUiFixture _fixture;
    public ZzAudit(E2EUiFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Probe()
    {
        await E2ESeeder.EnsureSeededAsync();
        var dir = Environment.GetEnvironmentVariable("SHOT_DIR")!;
        var lines = new List<string>();

        foreach (var route in new[] { "/insights", "/", "/recap" })
        {
            var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, route);
            await using var _ = page.Context;
            await page.WaitForTimeoutAsync(9000); // well past any load state

            var running = await page.EvaluateAsync<JsonElement>(@"() => {
                const out = [];
                // getAnimations() reports what is ACTUALLY running right now, including
                // animations started by class changes — no guessing from stylesheets.
                for (const a of document.getAnimations()) {
                    const t = a.effect && a.effect.target;
                    out.push({
                        name: a.animationName || (a.effect && a.effect.getComputedTiming && 'transition') || '?',
                        state: a.playState,
                        iterations: a.effect ? String(a.effect.getComputedTiming().iterations) : '?',
                        duration: a.effect ? a.effect.getComputedTiming().duration : 0,
                        target: t ? (t.tagName + '.' + (typeof t.className === 'string' ? t.className : '')).slice(0,110) : 'none'
                    });
                }
                return out;
            }");

            lines.Add($"### {route}\n{JsonSerializer.Serialize(running, new JsonSerializerOptions { WriteIndented = true })}");

            // Now watch for 8 seconds and record any animation that STARTS during that window —
            // a periodic flash must show up here.
            await page.EvaluateAsync(@"() => {
                window.__started = [];
                document.addEventListener('animationstart', e => {
                    const t = e.target;
                    window.__started.push(e.animationName + ' on ' + t.tagName + '.' +
                        (typeof t.className === 'string' ? t.className : ''));
                }, true);
                document.addEventListener('transitionstart', e => {
                    const t = e.target;
                    window.__started.push('TRANSITION ' + e.propertyName + ' on ' + t.tagName + '.' +
                        (typeof t.className === 'string' ? t.className : ''));
                }, true);
            }");
            await page.WaitForTimeoutAsync(8000);
            var started = await page.EvaluateAsync<string[]>("() => window.__started.slice(0, 40)");
            lines.Add($"STARTED DURING 8s IDLE WATCH ({started.Length}):\n  " + string.Join("\n  ", started.Distinct()));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "anim.md"), string.Join("\n\n", lines));
    }
}
