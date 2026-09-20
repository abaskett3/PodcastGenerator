using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PodcastGenerator.Domain.Scripts;
using PodcastGenerator.Infrastructure.Speech;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Architecture;

/// <summary>Checks the structural rules in <c>CLAUDE.md</c> and the spec: Clean Architecture references (AC-2), HTTP code only
/// in Infrastructure (AC-3), the Async and CancellationToken naming rules (AC-4), path building (AC-8) and no committed key
/// (AC-27).</summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Script).Assembly;
    private static readonly Assembly Application = typeof(PodcastGenerator.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(OpenRouterSpeechClient).Assembly;
    private static readonly Assembly Cli = typeof(PodcastGenerator.Cli.CommandLine).Assembly;

    private static IEnumerable<string> ProjectReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith("PodcastGenerator", StringComparison.Ordinal))
            .OrderBy(name => name);

    // AC-2: the dependency rule, checked on the compiled assemblies.
    [Fact]
    public void Domain_references_no_other_project()
    {
        Assert.Empty(ProjectReferences(Domain));
    }

    [Fact]
    public void Application_references_only_Domain()
    {
        Assert.Equal(["PodcastGenerator.Domain"], ProjectReferences(Application));
    }

    [Fact]
    public void Infrastructure_references_only_Application_and_Domain()
    {
        Assert.All(ProjectReferences(Infrastructure), name => Assert.Contains(name, new[] { "PodcastGenerator.Application", "PodcastGenerator.Domain" }));
        Assert.Contains("PodcastGenerator.Application", ProjectReferences(Infrastructure));
    }

    [Fact]
    public void Cli_composes_Application_and_Infrastructure()
    {
        Assert.Contains("PodcastGenerator.Application", ProjectReferences(Cli));
        Assert.Contains("PodcastGenerator.Infrastructure", ProjectReferences(Cli));
    }

    // AC-2: the project files say the same.
    [Fact]
    public void The_project_files_follow_the_dependency_rule()
    {
        Assert.Empty(ProjectReferencesInFile("src", "PodcastGenerator.Domain"));
        Assert.Equal(["PodcastGenerator.Domain"], ProjectReferencesInFile("src", "PodcastGenerator.Application"));
        Assert.Equal(["PodcastGenerator.Application"], ProjectReferencesInFile("src", "PodcastGenerator.Infrastructure"));
        Assert.Equal(["PodcastGenerator.Application", "PodcastGenerator.Infrastructure"], ProjectReferencesInFile("src", "PodcastGenerator.Cli"));
        Assert.True(File.Exists(RepositoryFiles.Combine("src", "PodcastGenerator.Cli", "PodcastGenerator.Cli.csproj")));
        Assert.Contains("tests", Directory.GetDirectories(RepositoryFiles.Root).Select(Path.GetFileName));
    }

    private static List<string> ProjectReferencesInFile(string folder, string project)
    {
        var path = RepositoryFiles.Combine(folder, project, project + ".csproj");
        return XDocument.Load(path).Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(((string)element.Attribute("Include")!).Replace('\\', '/')))
            .OrderBy(name => name)
            .ToList();
    }

    // AC-3: all HTTP code is in Infrastructure.
    [Fact]
    public void Only_Infrastructure_references_the_http_assembly()
    {
        static bool ReferencesHttp(Assembly assembly) =>
            assembly.GetReferencedAssemblies().Any(name => name.Name == "System.Net.Http");

        Assert.False(ReferencesHttp(Domain));
        Assert.False(ReferencesHttp(Application));
        Assert.False(ReferencesHttp(Cli));
        Assert.True(ReferencesHttp(Infrastructure));
    }

    [Fact]
    public void The_speech_client_interface_is_declared_in_Application()
    {
        var contract = Application.GetType("PodcastGenerator.Application.Abstractions.ISpeechClient");

        Assert.NotNull(contract);
        Assert.True(contract.IsAssignableFrom(typeof(OpenRouterSpeechClient)));
    }

    // AC-4: every async method ends in Async and takes a CancellationToken.
    [Fact]
    public void Every_async_method_ends_in_Async_and_accepts_a_CancellationToken()
    {
        var problems = new List<string>();
        foreach (var assembly in new[] { Domain, Application, Infrastructure, Cli })
        {
            foreach (var type in assembly.GetTypes().Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)))
            {
                const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (var method in type.GetMethods(All).Where(method => !method.IsSpecialName && !method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)))
                {
                    var returnsTask = IsTaskLike(method.ReturnType);
                    var endsInAsync = method.Name.EndsWith("Async", StringComparison.Ordinal);
                    var isDisposeAsync = method.Name == "DisposeAsync"; // the name and signature are fixed by IAsyncDisposable

                    if (returnsTask && !endsInAsync)
                    {
                        problems.Add($"{type.Name}.{method.Name} returns a task but does not end in Async");
                    }

                    if (endsInAsync && !returnsTask)
                    {
                        problems.Add($"{type.Name}.{method.Name} ends in Async but does not return a task");
                    }

                    if (returnsTask && !isDisposeAsync && method.GetParameters().All(parameter => parameter.ParameterType != typeof(CancellationToken)))
                    {
                        problems.Add($"{type.Name}.{method.Name} does not accept a CancellationToken");
                    }
                }
            }
        }

        Assert.Empty(problems);
    }

    private static bool IsTaskLike(Type type) =>
        type == typeof(Task) || type == typeof(ValueTask)
        || (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)));

    // AC-8: paths are built with Path.Combine from the user profile folder; no literal %USERPROFILE% or separator.
    [Fact]
    public void Source_code_builds_paths_with_Path_Combine_and_never_uses_a_literal_USERPROFILE_or_separator()
    {
        var problems = new List<string>();
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            if (text.Contains("%USERPROFILE%", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{file}: literal %USERPROFILE%");
            }

            foreach (var call in PathCombineCalls(text))
            {
                foreach (Match literal in Regex.Matches(call, @"""(?:[^""\\]|\\.)*"""))
                {
                    if (literal.Value.Contains('/') || literal.Value.Contains('\\'))
                    {
                        problems.Add($"{file}: separator in {literal.Value}");
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>The text of each <c>Path.Combine(...)</c> call, up to its closing parenthesis.</summary>
    private static IEnumerable<string> PathCombineCalls(string text)
    {
        const string Marker = "Path.Combine(";
        var index = 0;
        while ((index = text.IndexOf(Marker, index, StringComparison.Ordinal)) >= 0)
        {
            var start = index + Marker.Length;
            var depth = 1;
            var inString = false;
            var position = start;
            for (; position < text.Length && depth > 0; position++)
            {
                var character = text[position];
                if (inString)
                {
                    if (character == '\\')
                    {
                        position++;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }
                }
                else if (character == '"')
                {
                    inString = true;
                }
                else if (character == '(')
                {
                    depth++;
                }
                else if (character == ')')
                {
                    depth--;
                }
            }

            yield return text[start..Math.Min(position, text.Length)];
            index = start;
        }
    }

    [Fact]
    public void The_default_output_and_runtime_folders_use_the_UserProfile_special_folder()
    {
        var text = File.ReadAllText(RepositoryFiles.Combine("src", "PodcastGenerator.Infrastructure", "FileSystem", "RuntimePaths.cs"));

        Assert.Contains("Environment.SpecialFolder.UserProfile", text);
    }

    // AC-27: no real key is committed, and *.env files are ignored.
    [Fact]
    public void No_source_document_or_collection_contains_something_shaped_like_a_real_key()
    {
        var keyShape = new Regex(@"sk-or-[A-Za-z0-9_\-]{16,}");
        var problems = new List<string>();
        foreach (var file in ScannedFiles())
        {
            if (keyShape.IsMatch(File.ReadAllText(file)))
            {
                problems.Add(file);
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Gitignore_ignores_env_files()
    {
        var lines = File.ReadAllLines(RepositoryFiles.Combine(".gitignore")).Select(line => line.Trim()).ToList();

        Assert.Contains("*.env", lines);
    }

    [Fact]
    public void The_product_never_names_config_env_or_the_claude_credentials_folder()
    {
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("config.env", text);
            Assert.DoesNotContain("credentials", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // AC-55: the Postman collection carries the key as a variable.
    [Fact]
    public void The_postman_collection_uses_a_key_variable_and_no_real_key()
    {
        var files = Directory.GetFiles(RepositoryFiles.Combine("postman"), "*.json");
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.Contains("{{OPENROUTER_API_KEY}}", text);
            Assert.Contains("/audio/speech", text);
            Assert.DoesNotMatch(@"sk-or-", text);
        }
    }

    private static IEnumerable<string> SourceFiles() => RepositoryScan.SourceFiles();

    private static IEnumerable<string> ScannedFiles() => RepositoryScan.ScannedFiles();
}
