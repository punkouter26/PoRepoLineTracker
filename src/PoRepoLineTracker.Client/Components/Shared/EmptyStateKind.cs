namespace PoRepoLineTracker.Client.Components.Shared;

/// <summary>
/// Icon + copy hints for <see cref="EmptyState"/>. Centralised here so every empty state in the
/// app draws the same picture for the same situation (no more <c>bar_chart</c> here, <c>insights</c>
/// there, <c>cloud</c> somewhere else for "no data").
/// </summary>
public enum EmptyStateKind
{
    /// <summary>Generic data panel with nothing to render. Default icon <c>bar_chart</c>.</summary>
    NoData,
    /// <summary>Repository list / detail has nothing tracked yet. Icon <c>storage</c>.</summary>
    NoRepositories,
    /// <summary>Insights / analytics page awaiting data. Icon <c>insights</c>.</summary>
    NoInsights,
    /// <summary>Connection diagnostics can't reach a dependency. Icon <c>cloud_off</c>.</summary>
    Offline,
    /// <summary>Target resource (typically a single repository) could not be found. Icon <c>search_off</c>.</summary>
    NotFound,
    /// <summary>Callers pass their own icon via <see cref="EmptyState.EmptyIcon"/>; this kind is for when
    /// the situation is unique enough not to fit a preset and <see cref="EmptyStateKind.NoData"/> would
    /// lie. The icon still has to be supplied; only the layout, sizing and copy are inherited.</summary>
    Custom,
}