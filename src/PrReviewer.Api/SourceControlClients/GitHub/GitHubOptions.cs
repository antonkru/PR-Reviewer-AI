namespace PrReviewer.Api.SourceControlClients.GitHub;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string AccessToken { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public string UserAgent { get; set; } = "PR-Reviewer-AI";

    public string BaseAddress { get; set; } = "https://api.github.com/";
}
