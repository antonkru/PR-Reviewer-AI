using System.Threading.Channels;
using PrReviewer.Domain.Abstractions;
using PrReviewer.Domain.Models;

namespace PrReviewer.Api.Infrastructure;

public sealed class ChannelReviewQueue : IReviewQueue
{
    private readonly Channel<ReviewJob> _channel = Channel.CreateUnbounded<ReviewJob>(
        new UnboundedChannelOptions { SingleReader = true });

    public ValueTask WriteAsync(ReviewJob job, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(job, ct);

    public IAsyncEnumerable<ReviewJob> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
