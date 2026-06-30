using FluentValidation;
using PoRepoLineTracker.Shared.Models.Dtos;

namespace PoRepoLineTracker.Shared.Validation;

/// <summary>Rule 2.2 — FluentValidation rules for the create-alert request contract.</summary>
public sealed class CreateAlertRuleRequestValidator : AbstractValidator<CreateAlertRuleRequest>
{
    public CreateAlertRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Metric).IsInEnum();
        RuleFor(x => x.Operator).IsInEnum();
        RuleFor(x => x.ThresholdValue).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Rule 2.2 — FluentValidation rules for a single bulk-add repository entry.</summary>
public sealed class BulkRepositoryDtoValidator : AbstractValidator<BulkRepositoryDto>
{
    public BulkRepositoryDtoValidator()
    {
        RuleFor(x => x.Owner).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RepoName).NotEmpty().MaximumLength(100);
    }
}
