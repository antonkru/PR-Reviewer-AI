namespace PrReviewer.Domain.Models;

public sealed record PullRequestRef(
    string Workspace,
    string RepoSlug,
    int PrId,
    string? Title);
