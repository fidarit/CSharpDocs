using CSharpDocs.Data;

namespace CSharpDocs.Tests;

public class MarkdownTests
{
    [Fact]
    public void Renders_Common_Markdown()
    {
        var html = Markdown.ToHtml("**bold** and *italic* and `code`");

        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
        Assert.Contains("<code>code</code>", html);
    }

    [Fact]
    public void Renders_Strikethrough_And_Marked()
    {
        var html = Markdown.ToHtml("~~устарело~~ и ==важно==");
        Assert.Contains("<del>устарело</del>", html);
        Assert.Contains("<mark>важно</mark>", html);
    }

    [Fact]
    public void Renders_Lists_And_Links()
    {
        var html = Markdown.ToHtml("- one\n- two\n\n[link](https://example.com)");
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>one</li>", html);
        Assert.Contains("href=\"https://example.com\"", html);
    }

    [Fact]
    public void Raw_Html_Is_Escaped()
    {
        var html = Markdown.ToHtml("<script>alert(1)</script>");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Unsafe_Link_Schemes_Are_Stripped()
    {
        var html = Markdown.ToHtml("[x](javascript:alert(1))");
        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public void Empty_Input_Is_Empty()
    {
        Assert.Equal("", Markdown.ToHtml(""));
        Assert.Equal("", Markdown.ToHtml("   "));
    }
}
