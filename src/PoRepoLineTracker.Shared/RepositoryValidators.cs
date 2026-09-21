using PoRepoLineTracker.Shared.Models.Dtos;

namespace PoRepoLineTracker.Shared.Models;

using PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// The bulk-add repository rules. This used to be a FluentValidation validator — two packages and
/// an assembly scan for four rules; now it is the four rules, in the shape the /bulk endpoint
/// needs. Returns null when the DTO is valid, otherwise one entry per invalid field for
/// <see cref="Microsoft.AspNetCore.Http.Results.ValidationProblem"/>.
/// </summary>
public static class RepositoryValidators
{
    private const int MaxLength = 100;

    public static Dictionary<string, string[]>? Validate(BulkRepositoryDto dto)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(dto.Owner))
            errors[nameof(dto.Owner)] = ["'Owner' must not be empty."];
        else if (dto.Owner.Length > MaxLength)
            errors[nameof(dto.Owner)] = [$"'Owner' must be {MaxLength} characters or fewer."];

        if (string.IsNullOrWhiteSpace(dto.RepoName))
            errors[nameof(dto.RepoName)] = ["'RepoName' must not be empty."];
        else if (dto.RepoName.Length > MaxLength)
            errors[nameof(dto.RepoName)] = [$"'RepoName' must be {MaxLength} characters or fewer."];

        return errors.Count == 0 ? null : errors;
    }
}
