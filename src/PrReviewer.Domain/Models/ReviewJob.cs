namespace PrReviewer.Domain.Models;

public sealed record ReviewJob(
    PullRequestRef Pr,
    string HeadCommitSha,
    DateTimeOffset EnqueuedAt);
