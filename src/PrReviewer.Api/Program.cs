using System.Net.Http.Headers;
using PrReviewer.Agents;
using PrReviewer.Api.Bitbucket;
using PrReviewer.Api.Endpoints;
using PrReviewer.Api.Infrastructure;
using PrReviewer.Domain.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<BitbucketOptions>()
    .Bind(builder.Configuration.GetSection(BitbucketOptions.SectionName));

builder.Services.AddSingleton<WebhookSignatureValidator>();
builder.Services.AddSingleton<IReviewQueue, ChannelReviewQueue>();

builder.Services.AddHttpClient<ISourceControlClient, BitbucketClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BitbucketOptions>>().Value;
    http.BaseAddress = new Uri("https://api.bitbucket.org/2.0/");
    if (!string.IsNullOrEmpty(opts.AccessToken))
    {
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", opts.AccessToken);
    }
    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddReviewerAgents(builder.Configuration);

var app = builder.Build();

app.MapBitbucketWebhook();

app.MapGet("/", () => Results.Ok(new { service = "PR-Reviewer-AI", status = "ok" }));

app.MapGet("/health", () => Results.Ok(new
{
    service = "PR-Reviewer-AI",
    status = "ok",
    timestamp = DateTimeOffset.UtcNow,
}));

app.Run();
