using System.Text.Json.Serialization;

namespace PrReviewer.Api.Bitbucket;

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
