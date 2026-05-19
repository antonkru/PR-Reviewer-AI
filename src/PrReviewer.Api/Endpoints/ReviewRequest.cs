namespace PrReviewer.Api.Endpoints;

internal sealed record ReviewRequest(
    string? Provider,
    string? Owner,
    string? Repo,
    int PrId,
    string? HeadSha,
    string? Title);
