using System.Reflection;

namespace PrReviewer.Agents.Internal;

internal static class EmbeddedPromptLoader
{
    public static string Load(string fileName)
    {
        var assembly = typeof(EmbeddedPromptLoader).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Prompts.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded prompt '{resourceName}' not found. " +
                $"Available: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
