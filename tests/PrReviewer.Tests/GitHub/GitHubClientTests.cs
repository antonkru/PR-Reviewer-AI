using System.Net.Http.Headers;
using System.Text.Json;
using PrReviewer.Api.SourceControlClients.GitHub;
using PrReviewer.Domain.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace PrReviewer.Tests.GitHub;

public sealed class GitHubClientTests : IAsyncLifetime
{
    private const string Token = "secret-token";
    private const string AuthHeader = "Bearer " + Token;
    private const string UserAgent = "PR-Reviewer-AI";

    private WireMockServer _api = default!;
    private HttpClient _http = default!;

    public Task InitializeAsync()
    {
        _api = WireMockServer.Start();

        _http = new HttpClient
        {
            BaseAddress = new Uri(_api.Url! + "/"),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _http.Dispose();
        _api.Stop();
        _api.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Returns_diff_when_accept_diff_header_set()
    {
        const string diffBody = "diff --git a/x b/x\n--- a/x\n+++ b/x\n@@\n-old\n+new\n";

        _api
            .Given(Request.Create()
                .WithPath("/repos/acme/widgets/pulls/42")
                .UsingGet()
                .WithHeader("Authorization", AuthHeader)
                .WithHeader("Accept", "application/vnd.github.v3.diff")
                .WithHeader("User-Agent", UserAgent))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(diffBody));

        var client = new GitHubClient(_http);

        var diff = await client.GetPullRequestDiffAsync(
            new PullRequestRef("acme", "widgets", 42, null, Provider.GitHub),
            CancellationToken.None);

        Assert.Equal(diffBody, diff);

        var entry = Assert.Single(_api.LogEntries);
        Assert.Equal(AuthHeader, entry.RequestMessage.Headers!["Authorization"][0]);
        Assert.Contains("application/vnd.github.v3.diff", entry.RequestMessage.Headers["Accept"][0]);
        Assert.Equal(UserAgent, entry.RequestMessage.Headers["User-Agent"][0]);
    }

    [Fact]
    public async Task Throws_on_diff_error_status()
    {
        _api
            .Given(Request.Create()
                .WithPath("/repos/ws/repo/pulls/2")
                .UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var client = new GitHubClient(_http);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetPullRequestDiffAsync(
                new PullRequestRef("ws", "repo", 2, null, Provider.GitHub),
                CancellationToken.None));
    }

    [Fact]
    public async Task Posts_issue_comment_with_body_json()
    {
        const string markdown = "## Review\nLooks good.";

        _api
            .Given(Request.Create()
                .WithPath("/repos/acme/widgets/issues/7/comments")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201));

        var client = new GitHubClient(_http);

        await client.PostPrCommentAsync(
            new PullRequestRef("acme", "widgets", 7, null, Provider.GitHub),
            markdown,
            CancellationToken.None);

        var entry = Assert.Single(_api.LogEntries);
        Assert.Equal(AuthHeader, entry.RequestMessage.Headers!["Authorization"][0]);

        using var doc = JsonDocument.Parse(entry.RequestMessage.Body!);
        Assert.Equal(markdown, doc.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Throws_when_comment_post_returns_error()
    {
        _api
            .Given(Request.Create()
                .WithPath("/repos/ws/repo/issues/3/comments")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(403));

        var client = new GitHubClient(_http);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.PostPrCommentAsync(
                new PullRequestRef("ws", "repo", 3, null, Provider.GitHub),
                "anything",
                CancellationToken.None));
    }

    [Fact]
    public async Task Get_pull_request_comments_follows_link_header_pagination()
    {
        var nextUrl = _api.Url + "/repos/ws/repo/issues/5/comments?page=2&per_page=100";

        _api
            .Given(Request.Create()
                .WithPath("/repos/ws/repo/issues/5/comments")
                .WithParam("per_page", "100")
                .WithParam(p => !p.ContainsKey("page"))
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithHeader("Link", $"<{nextUrl}>; rel=\"next\", <{nextUrl}>; rel=\"last\"")
                .WithBody(JsonSerializer.Serialize(new[]
                {
                    new { body = "first page comment" },
                })));

        _api
            .Given(Request.Create()
                .WithPath("/repos/ws/repo/issues/5/comments")
                .WithParam("page", "2")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new[]
                {
                    new { body = "second page comment" },
                })));

        var client = new GitHubClient(_http);
        var comments = await client.GetPullRequestCommentsAsync(
            new PullRequestRef("ws", "repo", 5, null, Provider.GitHub),
            CancellationToken.None);

        Assert.Equal(2, comments.Count);
        Assert.Equal("first page comment", comments[0].Body);
        Assert.Equal("second page comment", comments[1].Body);
    }

    [Fact]
    public async Task Get_pull_request_comments_returns_empty_list_when_array_empty()
    {
        _api
            .Given(Request.Create()
                .WithPath("/repos/ws/repo/issues/6/comments")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("[]"));

        var client = new GitHubClient(_http);
        var comments = await client.GetPullRequestCommentsAsync(
            new PullRequestRef("ws", "repo", 6, null, Provider.GitHub),
            CancellationToken.None);

        Assert.Empty(comments);
    }

    [Fact]
    public async Task Get_pull_request_comments_throws_on_error_status()
    {
        _api
            .Given(Request.Create()
                .WithPath("/repos/ws/repo/issues/8/comments")
                .UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var client = new GitHubClient(_http);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetPullRequestCommentsAsync(
                new PullRequestRef("ws", "repo", 8, null, Provider.GitHub),
                CancellationToken.None));
    }
}
