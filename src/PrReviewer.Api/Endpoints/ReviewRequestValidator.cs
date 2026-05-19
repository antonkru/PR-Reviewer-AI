using FluentValidation;
using PrReviewer.Domain.Models;

namespace PrReviewer.Api.Endpoints;

internal sealed class ReviewRequestValidator : AbstractValidator<ReviewRequest>
{
    public ReviewRequestValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty()
            .WithMessage("Provider is required");

        RuleFor(x => x.Provider)
            .Must(BeKnownProvider!)
            .When(x => !string.IsNullOrEmpty(x.Provider))
            .WithMessage(x => $"Unknown provider '{x.Provider}'");

        RuleFor(x => x.Owner).NotEmpty();
        RuleFor(x => x.Repo).NotEmpty();
        RuleFor(x => x.PrId).GreaterThan(0);
    }

    private static bool BeKnownProvider(string value) =>
        Enum.TryParse<Provider>(value, ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed);
}
