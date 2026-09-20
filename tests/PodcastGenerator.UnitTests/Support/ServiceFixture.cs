using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Narration;
using PodcastGenerator.Application.Runtime;
using PodcastGenerator.Application.Scripts;
using PodcastGenerator.Infrastructure.FileSystem;

namespace PodcastGenerator.UnitTests.Support;

/// <summary>Builds the generation service from the real application classes and fakes for everything that touches the
/// outside world (network, disk, clock, waiting). Nothing here reads the user's key file or profile folders (AC-5).</summary>
internal sealed class ServiceFixture
{
    /// <summary>A fake user profile folder. Nothing is created there: the file system is in memory.</summary>
    public static readonly string UserProfile = Path.Combine(Path.GetTempPath(), "pg-fake-profile");

    public ServiceFixture(bool withKeyFile = true)
    {
        FileSystem = new InMemoryFileSystem();
        Paths = new RuntimePaths(UserProfile);
        Environment = new FakeEnvironmentVariables();
        Speech = new FakeSpeechClient();
        Workspaces = new FakeAudioWorkspaceFactory(FileSystem);
        Delayer = new FakeDelayer();
        Reporter = new RecordingReporter();
        Clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
        Defaults = new EmbeddedDefaultResources();

        if (withKeyFile)
        {
            FileSystem.Files[Paths.KeyFilePath] = $"OPENROUTER_API_KEY={TestKeys.Sentinel}\n";
        }

        ScriptPath = Path.Combine(UserProfile, "scripts", "episode.txt");
        FileSystem.Directories.Add(Path.GetDirectoryName(ScriptPath)!);
        FileSystem.Files[ScriptPath] = "CECIL: Good evening.\n";
    }

    public InMemoryFileSystem FileSystem { get; }

    public RuntimePaths Paths { get; }

    public FakeEnvironmentVariables Environment { get; }

    public FakeSpeechClient Speech { get; }

    public FakeAudioWorkspaceFactory Workspaces { get; }

    public FakeDelayer Delayer { get; }

    public RecordingReporter Reporter { get; }

    public FixedTimeProvider Clock { get; }

    public EmbeddedDefaultResources Defaults { get; }

    public string ScriptPath { get; }

    public string DefaultOutputDirectory => Paths.DefaultOutputDirectory;

    public string DefaultOutputPath => Path.Combine(DefaultOutputDirectory, "Podcast-09-19-2026.mp3");

    public void SetScript(string text) => FileSystem.Files[ScriptPath] = text;

    public PodcastGenerationService CreateService() =>
        new(
            FileSystem,
            Paths,
            new RuntimeInitializer(FileSystem, Paths, Defaults),
            new StyleProvider(FileSystem, Paths),
            new ApiKeyProvider(FileSystem, Paths, Environment),
            new ScriptParser(),
            new TranscriptRenderer(new TagVocabulary()),
            new ChunkPlanner(),
            Speech,
            Workspaces,
            new OutputPathResolver(FileSystem, Paths, Clock),
            Delayer,
            Reporter);

    public Task<GenerationResult> RunAsync(string? outputPath = null, CancellationToken cancellationToken = default) =>
        CreateService().GenerateAsync(new GenerationRequest(ScriptPath, outputPath), cancellationToken);
}

/// <summary>Finds files in the repository (docs, sources) from the test's location, so tests can read the sample script.</summary>
internal static class RepositoryFiles
{
    public static string Root { get; } = FindRoot();

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PodcastGenerator.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root (PodcastGenerator.sln) was not found above the test binaries.");
    }
}
