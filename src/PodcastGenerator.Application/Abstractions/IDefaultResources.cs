namespace PodcastGenerator.Application.Abstractions;

/// <summary>Default resources embedded in the executable. They are copied to the runtime folder on the first run.</summary>
public interface IDefaultResources
{
    /// <summary>The default narration prompt template (see <c>docs/style-guide.md</c>, section 9.1).</summary>
    string DefaultStyle { get; }
}
