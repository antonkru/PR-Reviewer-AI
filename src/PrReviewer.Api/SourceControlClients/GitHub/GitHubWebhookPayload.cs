using System.Text.Json.Serialization;

namespace PrReviewer.Api.SourceControlClients.GitHub;

internal sealed class GitHubWebhookPayload
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("pull_request")]
    public GitHubPullRequestPayload? PullRequest { get; set; }

    [JsonPropertyName("repository")]
    public GitHubRepositoryPayload? Repository { get; set; }
}

internal sealed class GitHubPullRequestPayload
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("head")]
    public GitHubCommitRefPayload? Head { get; set; }
}

internal sealed class GitHubCommitRefPayload
{
    [JsonPropertyName("sha")]
    public string? Sha { get; set; }
}

internal sealed class GitHubRepositoryPayload
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("owner")]
    public GitHubOwnerPayload? Owner { get; set; }
}

internal sealed class GitHubOwnerPayload
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
}
