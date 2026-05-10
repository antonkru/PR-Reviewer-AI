using System.ComponentModel.DataAnnotations;

namespace PrReviewer.Agents.Configuration;

public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Model { get; set; } = "gpt-4o-mini";

    [Range(1_000, 1_000_000)]
    public int MaxDiffChars { get; set; } = 200_000;
}
