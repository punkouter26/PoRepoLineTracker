// Cross-cutting namespaces every slice may use. Deliberately excludes
// PoRepoLineTracker.API.Features.* — slices must not reference each other (Rule 2), so a slice
// that needs another slice's type has to import it explicitly and justify the coupling.
// Only the composition root (Extensions/) does that.
global using PoRepoLineTracker.API.Auth;
global using PoRepoLineTracker.API.Extensions;
global using PoRepoLineTracker.API.Middleware;
global using PoRepoLineTracker.API.Services;
global using PoRepoLineTracker.API.Storage;
global using PoRepoLineTracker.API.Telemetry;
global using PoRepoLineTracker.Shared.Domain;
global using PoRepoLineTracker.Shared.Models;
global using PoRepoLineTracker.Shared.Models.Dtos;
global using PoRepoLineTracker.Shared.Serialization;
global using PoRepoLineTracker.Shared.Validation;
