namespace PrReviewer.Api.Bitbucket;

public sealed class BitbucketOptions
{
    public const string SectionName = "Bitbucket";

    public string AccessToken { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public string BaseAddress { get; set; } = string.Empty;
}
