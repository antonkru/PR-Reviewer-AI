using Microsoft.Extensions.Logging.Abstractions;
using PrReviewer.Agents;
using PrReviewer.Domain.Models;
using PrReviewer.Tests.Fakes;

namespace PrReviewer.Tests;

public sealed class ReviewBackgroundServiceTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string OldSha = "fedcba9876543210fedcba9876543210fedcba98";

    private static readonly PullRequestRef SamplePr =
        new("acme", "widgets", 42, "Add widget", Provider.Bitbucket);

    [Fact]
    public async Task Fetches_diff_runs_review_and_posts_comment_with_marker()
    {
        var queue = new FakeReviewQueue();
        var sourceControl = new FakeSourceControlClient { DiffToReturn = "--- a/x\n+++ b/x" };
        var reviewer = new FakeReviewerAgent { Response = new ReviewResult("review markdown", false) };

        await queue.WriteAsync(new ReviewJob(SamplePr, Sha, DateTimeOffset.UtcNow));
        queue.Complete();

        await RunUntilDrained(queue, sourceControl, reviewer);

        Assert.Equal(new[] { "--- a/x\n+++ b/x" }, reviewer.ReceivedDiffs);
        var posted = Assert.Single(sourceControl.PostedComments);
        Assert.Equal(SamplePr, posted.Pr);
        Assert.StartsWith("review markdown", posted.Markdown);
        Assert.EndsWith($"<!-- pr-reviewer-ai: sha={Sha} -->", posted.Markdown);
    }

    [Fact]
    public async Task Skips_when_marker_for_current_sha_already_present()
    {
        var queue = new FakeReviewQueue();
        var sourceControl = new FakeSourceControlClient { DiffToReturn = "diff" };
        sourceControl.Comments.Add(new PullRequestComment(
            $"earlier review body\n\n<!-- pr-reviewer-ai: sha={Sha} -->"));
        var reviewer = new FakeReviewerAgent();

        await queue.WriteAsync(new ReviewJob(SamplePr, Sha, DateTimeOffset.UtcNow));
        queue.Complete();

        await RunUntilDrained(queue, sourceControl, reviewer);

        Assert.Empty(reviewer.ReceivedDiffs);
        Assert.Empty(sourceControl.PostedComments);
    }

    [Fact]
    public async Task Reviews_when_existing_marker_is_for_a_different_sha()
    {
        var queue = new FakeReviewQueue();
        var sourceControl = new FakeSourceControlClient { DiffToReturn = "diff" };
        sourceControl.Comments.Add(new PullRequestComment(
            $"old review body\n\n<!-- pr-reviewer-ai: sha={OldSha} -->"));
        var reviewer = new FakeReviewerAgent { Response = new ReviewResult("fresh review", false) };

        await queue.WriteAsync(new ReviewJob(SamplePr, Sha, DateTimeOffset.UtcNow));
        queue.Complete();

        await RunUntilDrained(queue, sourceControl, reviewer);

        Assert.Single(reviewer.ReceivedDiffs);
        var posted = Assert.Single(sourceControl.PostedComments);
        Assert.Contains($"<!-- pr-reviewer-ai: sha={Sha} -->", posted.Markdown);
        Assert.DoesNotContain(OldSha, posted.Markdown);
    }

    [Fact]
    public async Task Two_jobs_for_same_sha_only_post_once()
    {
        var queue = new FakeReviewQueue();
        var sourceControl = new RecordingSourceControl { DiffToReturn = "diff" };
        var reviewer = new FakeReviewerAgent { Response = new ReviewResult("LGTM", false) };

        await queue.WriteAsync(new ReviewJob(SamplePr, Sha, DateTimeOffset.UtcNow));
        await queue.WriteAsync(new ReviewJob(SamplePr, Sha, DateTimeOffset.UtcNow));
        queue.Complete();

        await RunUntilDrained(queue, sourceControl, reviewer);

        Assert.Single(reviewer.ReceivedDiffs);
        Assert.Single(sourceControl.PostedComments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Skips_review_when_diff_is_empty_or_whitespace(string diff)
    {
        var queue = new FakeReviewQueue();
        var sourceControl = new FakeSourceControlClient { DiffToReturn = diff };
        var reviewer = new FakeReviewerAgent();

        await queue.WriteAsync(new ReviewJob(SamplePr, Sha, DateTimeOffset.UtcNow));
        queue.Complete();

        await RunUntilDrained(queue, sourceControl, reviewer);

        Assert.Empty(reviewer.ReceivedDiffs);
        Assert.Empty(sourceControl.PostedComments);
    }

    [Fact]
    public async Task Continues_processing_after_one_job_throws()
    {
        var queue = new FakeReviewQueue();
        var sourceControl = new FakeSourceControlClient { DiffToReturn = "diff" };
        var calls = 0;
        var reviewer = new FakeReviewerAgent
        {
            ResponseFactory = _ =>
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("boom");
                return new ReviewResult("ok", false);
            },
        };

        await queue.WriteAsync(new ReviewJob(SamplePr, Sha, DateTimeOffset.UtcNow));
        await queue.WriteAsync(new ReviewJob(SamplePr with { PrId = 43 }, Sha, DateTimeOffset.UtcNow));
        queue.Complete();

        await RunUntilDrained(queue, sourceControl, reviewer);

        Assert.Equal(2, reviewer.ReceivedDiffs.Count);
        var posted = Assert.Single(sourceControl.PostedComments);
        Assert.Equal(43, posted.Pr.PrId);
        Assert.StartsWith("ok", posted.Markdown);
    }

    [Fact]
    public async Task Dispatches_to_correct_client_per_job_provider()
    {
        var queue = new FakeReviewQueue();
        var bitbucket = new FakeSourceControlClient { DiffToReturn = "bb-diff" };
        var github = new FakeSourceControlClient { DiffToReturn = "gh-diff" };
        var factory = new FakeSourceControlClientFactory();
        factory.Clients[Provider.Bitbucket] = bitbucket;
        factory.Clients[Provider.GitHub] = github;
        var reviewer = new FakeReviewerAgent { Response = new ReviewResult("ok", false) };

        var bbPr = new PullRequestRef("acme", "widgets", 1, null, Provider.Bitbucket);
        var ghPr = new PullRequestRef("acme", "widgets", 2, null, Provider.GitHub);

        await queue.WriteAsync(new ReviewJob(bbPr, Sha, DateTimeOffset.UtcNow));
        await queue.WriteAsync(new ReviewJob(ghPr, Sha, DateTimeOffset.UtcNow));
        queue.Complete();

        await RunUntilDrained(queue, factory, reviewer);

        var bbPosted = Assert.Single(bitbucket.PostedComments);
        Assert.Equal(Provider.Bitbucket, bbPosted.Pr.Provider);
        var ghPosted = Assert.Single(github.PostedComments);
        Assert.Equal(Provider.GitHub, ghPosted.Pr.Provider);
    }

    private static async Task RunUntilDrained(
        FakeReviewQueue queue,
        FakeSourceControlClient sourceControl,
        FakeReviewerAgent reviewer)
    {
        await RunUntilDrained(queue, FakeSourceControlClientFactory.ForAll(sourceControl), reviewer);
    }

    private static async Task RunUntilDrained(
        FakeReviewQueue queue,
        FakeSourceControlClientFactory factory,
        FakeReviewerAgent reviewer)
    {
        var service = new ReviewBackgroundService(
            queue, factory, reviewer,
            NullLogger<ReviewBackgroundService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(cts.Token);
        await service.ExecuteTask!;
        await service.StopAsync(cts.Token);
    }

    /// <summary>
    /// Mirrors what a real source control client does on PostComment: the marker becomes part of
    /// future GetComments results, so a follow-up job for the same sha sees it and skips.
    /// </summary>
    private sealed class RecordingSourceControl : FakeSourceControlClient
    {
        public override async Task PostPrCommentAsync(PullRequestRef pr, string markdown, CancellationToken ct)
        {
            await base.PostPrCommentAsync(pr, markdown, ct);
            Comments.Add(new PullRequestComment(markdown));
        }
    }
}
