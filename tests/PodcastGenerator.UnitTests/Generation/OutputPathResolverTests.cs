using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Infrastructure.FileSystem;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Generation;

public class OutputPathResolverTests
{
    private readonly InMemoryFileSystem _fileSystem = new();
    private readonly RuntimePaths _paths = new(ServiceFixture.UserProfile);

    private OutputPathResolver Create(DateTimeOffset? now = null) =>
        new(_fileSystem, _paths, new FixedTimeProvider(now ?? new DateTimeOffset(2026, 9, 19, 8, 30, 0, TimeSpan.Zero)));

    // AC-8, AC-9
    [Fact]
    public void The_default_is_Podcast_MM_DD_YYYY_in_the_default_directory()
    {
        var target = Create().Resolve(null);

        Assert.Equal(_paths.DefaultOutputDirectory, target.Directory);
        Assert.Equal(Path.Combine(_paths.DefaultOutputDirectory, "Podcast-09-19-2026.mp3"), target.PathFor(1));
    }

    [Theory]
    [InlineData(2026, 1, 5, "Podcast-01-05-2026.mp3")]
    [InlineData(2027, 12, 31, "Podcast-12-31-2027.mp3")]
    public void The_date_is_month_day_year_with_two_digits_each(int year, int month, int day, string expected)
    {
        var target = Create(new DateTimeOffset(year, month, day, 10, 0, 0, TimeSpan.Zero)).Resolve("");

        Assert.Equal(expected, Path.GetFileName(target.PathFor(1)));
    }

    // AC-10
    [Fact]
    public void Later_attempts_add_a_hyphen_and_a_number_before_the_extension()
    {
        var target = Create().Resolve(null);

        Assert.Equal("Podcast-09-19-2026-2.mp3", Path.GetFileName(target.PathFor(2)));
        Assert.Equal("Podcast-09-19-2026-3.mp3", Path.GetFileName(target.PathFor(3)));
    }

    // AC-11
    [Fact]
    public void A_file_path_is_used_as_given()
    {
        var requested = Path.Combine(ServiceFixture.UserProfile, "out", "sub", "show.mp3");

        var target = Create().Resolve(requested);

        Assert.Equal(requested, target.PathFor(1));
        Assert.Equal(Path.GetDirectoryName(requested), target.Directory);
    }

    // AC-12
    [Fact]
    public void An_existing_directory_gets_the_default_file_name_inside_it()
    {
        var directory = Path.Combine(ServiceFixture.UserProfile, "existing");
        _fileSystem.Directories.Add(directory);

        var target = Create().Resolve(directory);

        Assert.Equal(Path.Combine(directory, "Podcast-09-19-2026.mp3"), target.PathFor(1));
    }

    // AC-13
    [Theory]
    [InlineData("show.wav", ".wav")]
    [InlineData("show.mp3.txt", ".txt")]
    [InlineData("show", "no extension")]
    public void A_file_path_that_is_not_mp3_is_rejected_and_names_the_required_extension(string name, string found)
    {
        var requested = Path.Combine(ServiceFixture.UserProfile, name);

        var exception = Assert.Throws<UserFacingException>(() => Create().Resolve(requested));

        Assert.Contains(".mp3", exception.Message);
        Assert.Contains(found, exception.Message);
    }

    [Fact]
    public void The_mp3_extension_is_matched_case_insensitively()
    {
        var requested = Path.Combine(ServiceFixture.UserProfile, "SHOW.MP3");

        Assert.Equal(requested, Create().Resolve(requested).PathFor(1));
    }

    [Fact]
    public void A_relative_path_is_resolved_against_the_current_directory()
    {
        var target = Create().Resolve(Path.Combine("relative", "show.mp3"));

        Assert.True(Path.IsPathRooted(target.Directory));
        Assert.Equal("show.mp3", Path.GetFileName(target.PathFor(1)));
    }
}
