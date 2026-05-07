using PrReviewer.Domain.Models;

namespace PrReviewer.Domain.Abstractions;

public interface ISourceControlClient
{
    Task<string> GetPullRequestDiffAsync(PullRequestRef pr, CancellationToken ct);
    Task PostPrCommentAsync(PullRequestRef pr, string markdown, CancellationToken ct);
}
