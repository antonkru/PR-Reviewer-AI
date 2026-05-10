namespace PrReviewer.Agents.Internal;

internal static class ReviewMarker
{
    private const string Tag = "pr-reviewer-ai";

    public static string Format(string sha) => $"<!-- {Tag}: sha={sha} -->";

    public static bool Matches(string commentBody, string sha)
        => commentBody.Contains($"{Tag}: sha={sha}", StringComparison.Ordinal);
}
