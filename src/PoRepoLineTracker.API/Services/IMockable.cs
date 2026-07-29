namespace PoRepoLineTracker.API.Services;

/// <summary>
/// Marker interface for services that serve mock/simulated data instead of a real backend.
/// </summary>
/// <remarks>
/// SOLID — Interface Segregation: a behaviourless marker keeps the "is this mock data?" concern
/// orthogonal to the service contracts it tags. When any <see cref="IMockable"/> implementation
/// is registered in DI, the API reports mock mode via /api/feature-flags and the UI surfaces the
/// "USING MOCK DATA" badge — so the indicator tracks the actual wiring, not just a config flag.
/// </remarks>
public interface IMockable
{
}
