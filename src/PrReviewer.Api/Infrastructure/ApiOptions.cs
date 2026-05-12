namespace PrReviewer.Api.Infrastructure;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public string AccessToken { get; set; } = string.Empty;
}
