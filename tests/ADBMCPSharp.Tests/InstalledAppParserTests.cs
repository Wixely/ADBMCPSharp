using ADBMCPSharp.Services;

namespace ADBMCPSharp.Tests;

public sealed class InstalledAppParserTests
{
    [Fact]
    public void ParseReturnsOnlyStrictPackageIdentifiers()
    {
        const string output = """
            package:org.example.player
            package:com.android.settings
            malformed output
            package:invalid-package-name
            package:org.example.player
            """;

        Assert.Equal(["com.android.settings", "org.example.player"], InstalledAppParser.Parse(output, 10));
    }

    [Fact]
    public void ParseBoundsResults()
    {
        const string output = """
            package:org.example.one
            package:org.example.two
            package:org.example.three
            """;

        Assert.Equal(2, InstalledAppParser.Parse(output, 2).Count);
    }
}
