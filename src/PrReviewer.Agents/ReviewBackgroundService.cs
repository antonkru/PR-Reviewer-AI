using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrReviewer.Agents.Internal;
using PrReviewer.Domain.Abstractions;

namespace PrReviewer.Agents;

public sealed class ReviewBackgroundService : BackgroundService
{
    private readonly IReviewQueue _queue;
    private readonly ISourceControlClient _sourceControl;
    private readonly IReviewerAgent _reviewer;
    private readonly ILogger<ReviewBackgroundService> _logger;

    public ReviewBackgroundService(
        IReviewQueue queue,
        ISourceControlClient sourceControl,
        IReviewerAgent reviewer,
        ILogger<ReviewBackgroundService> logger)
    {
        _queue = queue;
        _sourceControl = sourceControl;
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
                    "Reviewing PR {Workspace}/{Repo}#{PrId} sha={Sha} (enqueued {EnqueuedAt:O})",
                    job.Pr.Workspace, job.Pr.RepoSlug, job.Pr.PrId, job.HeadCommitSha, job.EnqueuedAt);

                var comments = await _sourceControl.GetPullRequestCommentsAsync(job.Pr, stoppingToken);
                if (comments.Any(c => ReviewMarker.Matches(c.Body, job.HeadCommitSha)))
                {
                    _logger.LogInformation(
                        "Skip {Workspace}/{Repo}#{PrId}: sha {Sha} already reviewed",
                        job.Pr.Workspace, job.Pr.RepoSlug, job.Pr.PrId, job.HeadCommitSha);
                    continue;
                }

                var diff = await _sourceControl.GetPullRequestDiffAsync(job.Pr, stoppingToken);
                if (string.IsNullOrWhiteSpace(diff))
                {
                    _logger.LogWarning(
                        "Empty diff for PR {Workspace}/{Repo}#{PrId} sha={Sha} — skipping",
                        job.Pr.Workspace, job.Pr.RepoSlug, job.Pr.PrId, job.HeadCommitSha);
                    continue;
                }

                var result = await _reviewer.ReviewAsync(diff, stoppingToken);
                var body = $"{result.Markdown}\n\n{ReviewMarker.Format(job.HeadCommitSha)}";
                await _sourceControl.PostPrCommentAsync(job.Pr, body, stoppingToken);

                _logger.LogInformation(
                    "Posted review comment on PR {Workspace}/{Repo}#{PrId} sha={Sha}",
                    job.Pr.Workspace, job.Pr.RepoSlug, job.Pr.PrId, job.HeadCommitSha);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Review failed for PR {Workspace}/{Repo}#{PrId} sha={Sha}",
                    job.Pr.Workspace, job.Pr.RepoSlug, job.Pr.PrId, job.HeadCommitSha);
            }
        }
    }
}
