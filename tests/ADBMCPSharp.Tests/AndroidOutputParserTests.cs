using ADBMCPSharp.Services;

namespace ADBMCPSharp.Tests;

public sealed class AndroidOutputParserTests
{
    [Fact]
    public void ParsePower_RecognizesAwakeDisplay()
    {
        var parsed = AndroidOutputParser.ParsePower("mWakefulness=Awake\nDisplay Power: state=ON");
        Assert.True(parsed.Awake);
        Assert.True(parsed.DisplayOn);
    }

    [Fact]
    public void ParsePower_RecognizesSleepingDisplay()
    {
        var parsed = AndroidOutputParser.ParsePower("mWakefulness=Asleep\nmHoldingDisplaySuspendBlocker=false");
        Assert.False(parsed.Awake);
        Assert.False(parsed.DisplayOn);
    }

    [Theory]
    [InlineData("mCurrentFocus=Window{2d u0 org.example.player/.MainActivity}", "org.example.player")]
    [InlineData("mFocusedApp=ActivityRecord{8 u0 com.example.app/.Home t2}", "com.example.app")]
    public void ParseForegroundPackage_ReturnsOnlyPackage(string output, string expected) =>
        Assert.Equal(expected, AndroidOutputParser.ParseForegroundPackage(output));
}
