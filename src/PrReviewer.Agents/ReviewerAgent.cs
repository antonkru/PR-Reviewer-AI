using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using PrReviewer.Agents.Configuration;
using PrReviewer.Agents.Internal;
using PrReviewer.Domain.Abstractions;
using PrReviewer.Domain.Models;

namespace PrReviewer.Agents;

public sealed class ReviewerAgent : IReviewerAgent
{
    private const string TruncationNotice = "[diff truncated — showing first {0} of {1} characters]\n\n";

    private readonly AIAgent _agent;
    private readonly int _maxDiffChars;
    private readonly ILogger<ReviewerAgent> _logger;

    public ReviewerAgent(IOptions<OpenAIOptions> options, ILogger<ReviewerAgent> logger)
    {
        var opts = options.Value;
        _maxDiffChars = opts.MaxDiffChars;
        _logger = logger;

        var prompt = EmbeddedPromptLoader.Load("SeniorDeveloperPrompt.md");
        var chatClient = new OpenAIClient(opts.ApiKey).GetChatClient(opts.Model);
        _agent = chatClient.AsAIAgent(instructions: prompt, name: "PR Reviewer");
    }

    public async Task<ReviewResult> ReviewAsync(string unifiedDiff, CancellationToken ct)
    {
        var (input, truncated) = TruncateIfNeeded(unifiedDiff);
        _logger.LogInformation(
            "Sending diff to LLM: {Chars} chars (truncated={Truncated})",
            input.Length, truncated);

        var response = await _agent.RunAsync(input, cancellationToken: ct);
        return new ReviewResult(response.Text, truncated);
    }

    private (string Input, bool Truncated) TruncateIfNeeded(string diff)
    {
        if (diff.Length <= _maxDiffChars)
            return (diff, false);

        var notice = string.Format(TruncationNotice, _maxDiffChars, diff.Length);
        return (notice + diff[.._maxDiffChars], true);
    }
}
