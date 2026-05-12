using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using PrReviewer.Domain.Abstractions;
using PrReviewer.Domain.Models;

namespace PrReviewer.Api.GitHub;

public sealed class GitHubClient : ISourceControlClient
{
    private const int MaxCommentPages = 5;
    private const int CommentsPerPage = 100;
    private const string DiffMediaType = "application/vnd.github.v3.diff";

    private static readonly Regex NextLinkRegex = new(
        "<(?<url>[^>]+)>;\\s*rel=\"next\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public GitHubClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> GetPullRequestDiffAsync(PullRequestRef pr, CancellationToken ct)
    {
        var url = new Uri(_http.BaseAddress!, $"repos/{pr.Owner}/{pr.RepoSlug}/pulls/{pr.PrId}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(DiffMediaType));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<IReadOnlyList<PullRequestComment>> GetPullRequestCommentsAsync(
        PullRequestRef pr,
        CancellationToken ct)
    {
        var results = new List<PullRequestComment>();
        Uri? current = new(
            _http.BaseAddress!,
            $"repos/{pr.Owner}/{pr.RepoSlug}/issues/{pr.PrId}/comments?per_page={CommentsPerPage}");

        for (var page = 0; page < MaxCommentPages && current is not null; page++)
        {
            using var response = await _http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var entries = await response.Content.ReadFromJsonAsync<List<IssueComment>>(JsonOptions, ct);
            if (entries is not null)
            {
                foreach (var entry in entries)
                {
                    if (entry.Body is not null)
                        results.Add(new PullRequestComment(entry.Body));
                }
            }

            current = NextLink(response.Headers);
        }

        return results;
    }

    public async Task PostPrCommentAsync(PullRequestRef pr, string markdown, CancellationToken ct)
    {
        var url = $"repos/{pr.Owner}/{pr.RepoSlug}/issues/{pr.PrId}/comments";
        var body = new { body = markdown };
        using var response = await _http.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();
    }

    private static Uri? NextLink(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Link", out var values))
            return null;

        foreach (var value in values)
        {
            var match = NextLinkRegex.Match(value);
            if (match.Success)
                return new Uri(match.Groups["url"].Value);
        }
        return null;
    }

    private sealed record IssueComment(string? Body);
}
