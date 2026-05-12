using FluentValidation;

namespace PrReviewer.Api.GitHub;

internal sealed class GitHubWebhookPayloadValidator : AbstractValidator<GitHubWebhookPayload>
{
    public GitHubWebhookPayloadValidator()
    {
        RuleFor(x => x.Repository != null && x.Repository.Owner != null ? x.Repository.Owner.Login : null)
            .NotEmpty()
            .WithName("owner")
            .WithMessage("repository.owner.login is required");

        RuleFor(x => x.Repository != null ? x.Repository.Name : null)
            .NotEmpty()
            .WithName("repo")
            .WithMessage("repository.name is required");

        RuleFor(x => x.PullRequest != null ? x.PullRequest.Number : 0)
            .GreaterThan(0)
            .WithName("prId")
            .WithMessage("pull_request.number is required");

        RuleFor(x => x.PullRequest != null && x.PullRequest.Head != null ? x.PullRequest.Head.Sha : null)
            .NotEmpty()
            .WithName("headSha")
            .WithMessage("pull_request.head.sha is required (cannot dedup without it)");
    }
}
