using PrReviewer.Domain.Models;

namespace PrReviewer.Domain.Abstractions;

public interface IReviewerAgent
{
    Task<ReviewResult> ReviewAsync(string unifiedDiff, CancellationToken ct);
}
