using CSharpDocs;

namespace CSharpDocs.Tests;

public class VersionTests
{
    [Theory]
    [InlineData("/home/user/.nuget/packages/mylib/1.2.3/mylib.1.2.3.nupkg", "1.2.3")]
    [InlineData("/home/user/.nuget/packages/mylib/25.5.9-a0/mylib.25.5.9-a0.nupkg", "25.5.9-a0")]
    [InlineData("/projects/MyLib/bin/Debug/net8.0/MyLib.xml", "dev")]
    public void DetectVersion_FromPath(string path, string expected) =>
        Assert.Equal(expected, Options.DetectVersion(path));

    [Theory]
    [InlineData("mylib.1.2.3.nupkg", "1.2.3")]
    [InlineData("MyLib.2.0.0-beta.1.nupkg", "2.0.0-beta.1")]
    public void DetectVersion_FromFileName(string fileName, string expected) =>
        Assert.Equal(expected, Options.DetectVersion(fileName));

    [Fact]
    public void DetectVersion_Empty_Is_Dev() =>
        Assert.Equal("dev", Options.DetectVersion(""));

    [Fact]
    public void Mcp_Boolean_Flags_Accept_Bare_And_Explicit_Values()
    {
        Assert.True(Options.ParseBoolean(""));
        Assert.True(Options.ParseBoolean("true"));
        Assert.False(Options.ParseBoolean("false"));
    }

    [Fact]
    public void Mcp_Flags_Use_Environment_Unless_Argument_Is_Set()
    {
        Assert.True(Options.ReadBooleanOption(null, "true"));
        Assert.False(Options.ReadBooleanOption(null, "false"));
        Assert.False(Options.ReadBooleanOption("false", "true"));
        Assert.True(Options.ReadBooleanOption("", "false"));
    }
}
