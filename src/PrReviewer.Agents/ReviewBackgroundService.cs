using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrReviewer.Agents.Internal;
using PrReviewer.Domain.Abstractions;

namespace PrReviewer.Agents;

public sealed class ReviewBackgroundService : BackgroundService
{
    private readonly IReviewQueue _queue;
    private readonly ISourceControlClientFactory _sourceControlFactory;
    private readonly IReviewerAgent _reviewer;
    private readonly ILogger<ReviewBackgroundService> _logger;

    public ReviewBackgroundService(
        IReviewQueue queue,
        ISourceControlClientFactory sourceControlFactory,
        IReviewerAgent reviewer,
        ILogger<ReviewBackgroundService> logger)
    {
        _queue = queue;
        _sourceControlFactory = sourceControlFactory;
        _reviewer = reviewer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReviewBackgroundService started");

        await foreach (var job in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation(
                    "Reviewing {Provider} PR {Owner}/{Repo}#{PrId} sha={Sha} (enqueued {EnqueuedAt:O})",
                    job.Pr.Provider, job.Pr.Owner, job.Pr.RepoSlug, job.Pr.PrId, job.HeadCommitSha, job.EnqueuedAt);

                var sourceControl = _sourceControlFactory.For(job.Pr.Provider);

                var comments = await sourceControl.GetPullRequestCommentsAsync(job.Pr, stoppingToken);
                if (comments.Any(c => ReviewMarker.Matches(c.Body, job.HeadCommitSha)))
                {
                    _logger.LogInformation(
                        "Skip {Provider} {Owner}/{Repo}#{PrId}: sha {Sha} already reviewed",
                        job.Pr.Provider, job.Pr.Owner, job.Pr.RepoSlug, job.Pr.PrId, job.HeadCommitSha);
                    continue;
                }

                var diff = await sourceControl.GetPullRequestDiffAsync(job.Pr, stoppingToken);
                if (string.IsNullOrWhiteSpace(diff))
                {
                    _logger.LogWarning(
                        "Empty diff for {Provider} PR {Owner}/{Repo}#{PrId} sha={Sha} — skipping",
                        job.Pr.Provider, job.Pr.Owner, job.Pr.RepoSlug, job.Pr.PrId, job.HeadCommitSha);
                    continue;
                }

                var result = await _reviewer.ReviewAsync(diff, stoppingToken);
                var body = $"{result.Markdown}\n\n{ReviewMarker.Format(job.HeadCommitSha)}";
                await sourceControl.PostPrCommentAsync(job.Pr, body, stoppingToken);

                _logger.LogInformation(
                    "Posted review comment on {Provider} PR {Owner}/{Repo}#{PrId} sha={Sha}",
                    job.Pr.Provider, job.Pr.Owner, job.Pr.RepoSlug, job.Pr.PrId, job.HeadCommitSha);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Review failed for {Provider} PR {Owner}/{Repo}#{PrId} sha={Sha}",
                    job.Pr.Provider, job.Pr.Owner, job.Pr.RepoSlug, job.Pr.PrId, job.HeadCommitSha);
            }
        }
    }
}
