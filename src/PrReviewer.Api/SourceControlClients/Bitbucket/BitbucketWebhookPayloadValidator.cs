using FluentValidation;

namespace PrReviewer.Api.SourceControlClients.Bitbucket;

internal sealed class BitbucketWebhookPayloadValidator : AbstractValidator<WebhookPayload>
{
    public BitbucketWebhookPayloadValidator()
    {
        RuleFor(x => x.Repository != null ? x.Repository.Workspace != null ? x.Repository.Workspace.Slug : null : null)
            .NotEmpty()
            .WithName("owner")
            .WithMessage("repository.workspace.slug is required");

        RuleFor(x => x.Repository != null ? x.Repository.Name : null)
            .NotEmpty()
            .WithName("repo")
            .WithMessage("repository.name is required");

        RuleFor(x => x.PullRequest != null ? x.PullRequest.Id : 0)
            .GreaterThan(0)
            .WithName("prId")
            .WithMessage("pullrequest.id is required");

        RuleFor(x => x.PullRequest != null && x.PullRequest.Source != null && x.PullRequest.Source.Commit != null
                ? x.PullRequest.Source.Commit.Hash
                : null)
            .NotEmpty()
            .WithName("headSha")
            .WithMessage("pullrequest.source.commit.hash is required (cannot dedup without it)");
    }
}
