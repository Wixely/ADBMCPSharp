using ADBMCPSharp.Services;

namespace ADBMCPSharp.Tests;

public sealed class MediaSessionParserTests
{
    [Fact]
    public void ParsesAllowlistedMediaFields()
    {
        const string output = """
            Media button session is MediaSessionRecord{abc u0 org.example.player/session}
              active=true
              state=PlaybackState {state=3, position=12000, buffered position=15000, speed=1.0, updated=1, actions=7}
              android.media.metadata.TITLE=Example title
              android.media.metadata.ARTIST=Example artist
            """;

        var result = MediaSessionParser.Parse(output);

        Assert.Equal("org.example.player", result.Package);
        Assert.True(result.Active);
        Assert.Equal("Playing", result.PlaybackState);
        Assert.Equal(12000, result.PositionMilliseconds);
        Assert.Equal(1.0, result.Speed);
        Assert.Equal("Example title", result.Title);
        Assert.Equal("Example artist", result.Artist);
    }

    [Fact]
    public void MetadataIsBounded()
    {
        var result = MediaSessionParser.Parse("android.media.metadata.TITLE=" + new string('x', 300));
        Assert.Equal(256, result.Title!.Length);
    }

    [Fact]
    public void ParsesPackageFromMediaButtonLineWithAdditionalRecordFields()
    {
        var output = "Media button session is MediaSessionRecord{opaque token=1 owner=org.example.player/session}";

        var result = MediaSessionParser.Parse(output);

        Assert.Equal("org.example.player", result.Package);
    }
}
