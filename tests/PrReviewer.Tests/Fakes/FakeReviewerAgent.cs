using PrReviewer.Domain.Abstractions;
using PrReviewer.Domain.Models;

namespace PrReviewer.Tests.Fakes;

public sealed class FakeReviewerAgent : IReviewerAgent
{
    public List<string> ReceivedDiffs { get; } = new();

    public ReviewResult Response { get; set; } = new("LGTM", false);

    public Func<string, ReviewResult>? ResponseFactory { get; set; }

    public Task<ReviewResult> ReviewAsync(string unifiedDiff, CancellationToken ct)
    {
        ReceivedDiffs.Add(unifiedDiff);
        var result = ResponseFactory is null ? Response : ResponseFactory(unifiedDiff);
        return Task.FromResult(result);
    }
}
