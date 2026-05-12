namespace PrReviewer.Domain.Models;

/// <param name="HeadCommitSha">
/// The PR head commit SHA at enqueue time, used both for dedup (skip if a prior
/// review already tagged this SHA) and for the marker written into the posted
/// review comment. <c>null</c> means "force review": dedup is skipped and no
/// marker is emitted, so this run does not affect future webhook-driven reviews.
/// </param>
public sealed record ReviewJob(
    PullRequestRef Pr,
    string? HeadCommitSha,
    DateTimeOffset EnqueuedAt);
