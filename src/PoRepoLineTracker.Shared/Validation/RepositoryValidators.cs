using FluentValidation;
using PoRepoLineTracker.Shared.Models.Dtos;

namespace PoRepoLineTracker.Shared.Validation;

/// <summary>Rule 2.2 — FluentValidation rules for a single bulk-add repository entry.</summary>
public sealed class BulkRepositoryDtoValidator : AbstractValidator<BulkRepositoryDto>
{
    public BulkRepositoryDtoValidator()
    {
        RuleFor(x => x.Owner).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RepoName).NotEmpty().MaximumLength(100);
    }
}
