namespace PrReviewer.Domain.Models;

public sealed record ReviewJob(PullRequestRef Pr, DateTimeOffset EnqueuedAt);
