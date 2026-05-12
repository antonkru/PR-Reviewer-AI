using System.Text.Json.Serialization;

namespace PrReviewer.Api.SourceControlClients.Bitbucket;

internal sealed class WebhookPayload
{
    [JsonPropertyName("pullrequest")]
    public PullRequestPayload? PullRequest { get; set; }

    [JsonPropertyName("repository")]
    public RepositoryPayload? Repository { get; set; }
}

internal sealed class PullRequestPayload
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("source")]
    public PullRequestSourcePayload? Source { get; set; }
}

internal sealed class PullRequestSourcePayload
{
    [JsonPropertyName("commit")]
    public CommitPayload? Commit { get; set; }
}

internal sealed class CommitPayload
{
    [JsonPropertyName("hash")]
    public string? Hash { get; set; }
}

internal sealed class RepositoryPayload
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("workspace")]
    public WorkspacePayload? Workspace { get; set; }
}

internal sealed class WorkspacePayload
{
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}
