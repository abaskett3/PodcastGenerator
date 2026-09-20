using System.Reflection;

namespace PodcastGenerator.Cli;

/// <summary>The version reported by <c>--version</c>: the informational version of the executable. A release build gets it
/// from the release workflow (the <c>Version</c> MSBuild property), so it is exactly the release version (AC-17, AC-70).</summary>
public static class VersionInfo
{
    public static string Get(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        // Semantic Versioning build metadata ("+...") is not part of the version we report.
        var metadata = informational.IndexOf('+');
        return metadata >= 0 ? informational[..metadata] : informational;
    }
}
