using System.Reflection;
using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Infrastructure.FileSystem;

/// <summary>Default resources embedded in the assembly (and so in the single-file executable).</summary>
public sealed class EmbeddedDefaultResources : IDefaultResources
{
    private const string StyleResourceName = "PodcastGenerator.Resources.default-style.md";

    private readonly Lazy<string> _style = new(() => ReadResource(StyleResourceName));

    public string DefaultStyle => _style.Value;

    private static string ReadResource(string name)
    {
        using var stream = typeof(EmbeddedDefaultResources).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded resource '{name}' is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
