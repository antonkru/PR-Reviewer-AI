using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PrReviewer.Api.Bitbucket;
using PrReviewer.Domain.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace PrReviewer.Tests.Bitbucket;

public sealed class BitbucketClientTests : IAsyncLifetime
{
    private const string Token = "secret-token";
    private const string AuthHeader = "Bearer " + Token;

    private WireMockServer _api = default!;
    private WireMockServer _downloads = default!;
    private HttpClient _http = default!;

    public Task InitializeAsync()
    {
        _api = WireMockServer.Start();
        _downloads = WireMockServer.Start();

        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(_api.Url! + "/"),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _http.Dispose();
        _api.Stop();
        _api.Dispose();
        _downloads.Stop();
        _downloads.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Follows_cross_host_redirect_and_keeps_authorization_header()
    {
        const string diffBody = "diff --git a/x b/x\n--- a/x\n+++ b/x\n@@\n-old\n+new\n";

        _api
            .Given(Request.Create()
                .WithPath("/repositories/acme/widgets/pullrequests/42/diff")
                .UsingGet()
                .WithHeader("Authorization", AuthHeader))
            .RespondWith(Response.Create()
                .WithStatusCode((int)HttpStatusCode.Redirect)
                .WithHeader("Location", _downloads.Url + "/diff/payload"));

        _downloads
            .Given(Request.Create()
                .WithPath("/diff/payload")
                .UsingGet()
                .WithHeader("Authorization", AuthHeader))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(diffBody));

        var client = new BitbucketClient(_http);

        var diff = await client.GetPullRequestDiffAsync(
            new PullRequestRef("acme", "widgets", 42, null),
            CancellationToken.None);

        Assert.Equal(diffBody, diff);

        var firstHop = Assert.Single(_api.LogEntries);
        Assert.Equal(AuthHeader, firstHop.RequestMessage.Headers!["Authorization"][0]);

        var secondHop = Assert.Single(_downloads.LogEntries);
        Assert.Equal(AuthHeader, secondHop.RequestMessage.Headers!["Authorization"][0]);
    }

    [Fact]
    public async Task Returns_diff_body_on_direct_200()
    {
        _api
            .Given(Request.Create()
                .WithPath("/repositories/ws/repo/pullrequests/1/diff")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("the diff"));

        var client = new BitbucketClient(_http);

        var diff = await client.GetPullRequestDiffAsync(
            new PullRequestRef("ws", "repo", 1, null),
            CancellationToken.None);

        Assert.Equal("the diff", diff);
    }

    [Fact]
    public async Task Throws_when_redirect_loop_exceeds_hop_limit()
    {
        var path = "/repositories/ws/repo/pullrequests/9/diff";
        _api
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode((int)HttpStatusCode.Redirect)
                .WithHeader("Location", _api.Url + path));

        var client = new BitbucketClient(_http);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetPullRequestDiffAsync(
                new PullRequestRef("ws", "repo", 9, null),
                CancellationToken.None));
    }

    [Fact]
    public async Task Throws_on_diff_error_status()
    {
        _api
            .Given(Request.Create()
                .WithPath("/repositories/ws/repo/pullrequests/2/diff")
                .UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var client = new BitbucketClient(_http);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetPullRequestDiffAsync(
                new PullRequestRef("ws", "repo", 2, null),
                CancellationToken.None));
    }

    [Fact]
    public async Task Posts_comment_with_markdown_payload()
    {
        const string markdown = "## Review\nLooks good.";

        _api
            .Given(Request.Create()
                .WithPath("/repositories/acme/widgets/pullrequests/7/comments")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201));

        var client = new BitbucketClient(_http);

        await client.PostPrCommentAsync(
            new PullRequestRef("acme", "widgets", 7, null),
            markdown,
            CancellationToken.None);

        var entry = Assert.Single(_api.LogEntries);
        Assert.Equal(AuthHeader, entry.RequestMessage.Headers!["Authorization"][0]);

        using var doc = JsonDocument.Parse(entry.RequestMessage.Body!);
        Assert.Equal(markdown, doc.RootElement.GetProperty("content").GetProperty("raw").GetString());
    }

    [Fact]
    public async Task Throws_when_comment_post_returns_error()
    {
        _api
            .Given(Request.Create()
                .WithPath("/repositories/ws/repo/pullrequests/3/comments")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(403));

        var client = new BitbucketClient(_http);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.PostPrCommentAsync(
                new PullRequestRef("ws", "repo", 3, null),
                "anything",
                CancellationToken.None));
    }

    [Fact]
    public async Task Get_pull_request_comments_follows_next_pagination_links()
    {
        var nextUrl = _api.Url + "/repositories/ws/repo/pullrequests/5/comments?page=2&pagelen=100";

        _api
            .Given(Request.Create()
                .WithPath("/repositories/ws/repo/pullrequests/5/comments")
                .WithParam("pagelen", "100")
                .WithParam(p => !p.ContainsKey("page"))
                .UsingGet()
                .WithHeader("Authorization", AuthHeader))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    values = new[]
                    {
                        new { content = new { raw = "first page comment" } },
                    },
                    next = nextUrl,
                })));

        _api
            .Given(Request.Create()
                .WithPath("/repositories/ws/repo/pullrequests/5/comments")
                .WithParam("page", "2")
                .UsingGet()
                .WithHeader("Authorization", AuthHeader))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    values = new[]
                    {
                        new { content = new { raw = "second page comment" } },
                    },
                    next = (string?)null,
                })));

        var client = new BitbucketClient(_http);
        var comments = await client.GetPullRequestCommentsAsync(
            new PullRequestRef("ws", "repo", 5, null),
            CancellationToken.None);

        Assert.Equal(2, comments.Count);
        Assert.Equal("first page comment", comments[0].Body);
        Assert.Equal("second page comment", comments[1].Body);
    }

    [Fact]
    public async Task Get_pull_request_comments_returns_empty_list_when_no_values()
    {
        _api
            .Given(Request.Create()
                .WithPath("/repositories/ws/repo/pullrequests/6/comments")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"values\":[],\"next\":null}"));

        var client = new BitbucketClient(_http);
        var comments = await client.GetPullRequestCommentsAsync(
            new PullRequestRef("ws", "repo", 6, null),
            CancellationToken.None);

        Assert.Empty(comments);
    }

    [Fact]
    public async Task Get_pull_request_comments_throws_on_error_status()
    {
        _api
            .Given(Request.Create()
                .WithPath("/repositories/ws/repo/pullrequests/8/comments")
                .UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var client = new BitbucketClient(_http);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetPullRequestCommentsAsync(
                new PullRequestRef("ws", "repo", 8, null),
                CancellationToken.None));
    }
}
