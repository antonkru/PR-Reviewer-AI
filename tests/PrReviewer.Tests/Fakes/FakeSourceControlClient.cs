using PrReviewer.Domain.Abstractions;
using PrReviewer.Domain.Models;

namespace PrReviewer.Tests.Fakes;

public class FakeSourceControlClient : ISourceControlClient
{
    public string DiffToReturn { get; set; } = string.Empty;

    public Func<PullRequestRef, string>? DiffFactory { get; set; }

    public List<PullRequestComment> Comments { get; } = new();

    public List<(PullRequestRef Pr, string Markdown)> PostedComments { get; } = new();

    public virtual Task<string> GetPullRequestDiffAsync(PullRequestRef pr, CancellationToken ct)
    {
        var diff = DiffFactory is null ? DiffToReturn : DiffFactory(pr);
        return Task.FromResult(diff);
    }

    public virtual Task<IReadOnlyList<PullRequestComment>> GetPullRequestCommentsAsync(
        PullRequestRef pr,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PullRequestComment>>(Comments.ToList());

    public virtual Task PostPrCommentAsync(PullRequestRef pr, string markdown, CancellationToken ct)
    {
        PostedComments.Add((pr, markdown));
        return Task.CompletedTask;
    }
}
