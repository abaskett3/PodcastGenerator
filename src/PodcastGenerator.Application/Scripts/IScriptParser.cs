using PodcastGenerator.Domain.Scripts;

namespace PodcastGenerator.Application.Scripts;

public interface IScriptParser
{
    /// <summary>Reads a script written as <c>docs/script-writing-guide.md</c> describes and keeps only what is narrated.
    /// It never rewrites the spoken words and never checks that the script follows the guide.</summary>
    Script Parse(string text);
}
