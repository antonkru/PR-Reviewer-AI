using PrReviewer.Domain.Models;

namespace PrReviewer.Domain.Abstractions;

public interface IReviewQueue
{
    ValueTask WriteAsync(ReviewJob job, CancellationToken ct = default);
    IAsyncEnumerable<ReviewJob> ReadAllAsync(CancellationToken ct);
}
