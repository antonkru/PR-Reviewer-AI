namespace PrReviewer.Domain.Models;

public sealed record PullRequestRef(
    string Owner,
    string RepoSlug,
    int PrId,
    string? Title,
    Provider Provider);
