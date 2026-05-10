using System.Net.Http.Json;
using PrReviewer.Domain.Abstractions;
using PrReviewer.Domain.Models;

namespace PrReviewer.Api.Bitbucket;

public sealed class BitbucketClient : ISourceControlClient
{
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
        Uri current = new(_http.BaseAddress!, $"repositories/{pr.Workspace}/{pr.RepoSlug}/pullrequests/{pr.PrId}/diff");
        for (var hop = 0; hop < 5; hop++)
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

    public async Task PostPrCommentAsync(PullRequestRef pr, string markdown, CancellationToken ct)
    {
        var url = $"repositories/{pr.Workspace}/{pr.RepoSlug}/pullrequests/{pr.PrId}/comments";
        var body = new { content = new { raw = markdown } };
        using var response = await _http.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();
    }
}
