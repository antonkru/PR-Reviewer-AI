using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrReviewer.Domain.Abstractions;
using PrReviewer.Domain.Models;

namespace PrReviewer.Api.Bitbucket;

public sealed class BitbucketClient : ISourceControlClient
{
    private const int MaxRedirectHops = 5;
    private const int MaxCommentPages = 5;
    private const int CommentsPageLen = 100;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public BitbucketClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> GetPullRequestDiffAsync(PullRequestRef pr, CancellationToken ct)
    {
        // Bitbucket 302-redirects this endpoint to a download URL on a different host.
        // HttpClient drops the Authorization header on cross-host redirects, so auto-redirect
        // is disabled at the handler and we follow hops manually to keep the Bearer token attached.
        Uri current = new(_http.BaseAddress!, $"repositories/{pr.Owner}/{pr.RepoSlug}/pullrequests/{pr.PrId}/diff");
        for (var hop = 0; hop < MaxRedirectHops; hop++)
        {
            using var response = await _http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
        throw new HttpRequestException("Too many redirects fetching Bitbucket diff");
    }

    public async Task<IReadOnlyList<PullRequestComment>> GetPullRequestCommentsAsync(
        PullRequestRef pr,
        CancellationToken ct)
    {
        var results = new List<PullRequestComment>();
        Uri? current = new(
            _http.BaseAddress!,
            $"repositories/{pr.Owner}/{pr.RepoSlug}/pullrequests/{pr.PrId}/comments?pagelen={CommentsPageLen}");

        for (var page = 0; page < MaxCommentPages && current is not null; page++)
        {
            using var response = await _http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<CommentsPage>(JsonOptions, ct);
            if (payload?.Values is { } values)
            {
                foreach (var entry in values)
                {
                    var raw = entry.Content?.Raw;
                    if (raw is not null) results.Add(new PullRequestComment(raw));
                }
            }

            current = string.IsNullOrEmpty(payload?.Next) ? null : new Uri(payload!.Next!);
        }

        return results;
    }

    public async Task PostPrCommentAsync(PullRequestRef pr, string markdown, CancellationToken ct)
    {
        var url = $"repositories/{pr.Owner}/{pr.RepoSlug}/pullrequests/{pr.PrId}/comments";
        var body = new { content = new { raw = markdown } };
        using var response = await _http.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();
    }

    private sealed record CommentsPage(
        [property: JsonPropertyName("values")] List<CommentEntry>? Values,
        [property: JsonPropertyName("next")] string? Next);

    private sealed record CommentEntry(
        [property: JsonPropertyName("content")] CommentContent? Content);

    private sealed record CommentContent(
        [property: JsonPropertyName("raw")] string? Raw);
}
