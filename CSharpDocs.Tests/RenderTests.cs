using CSharpDocs.Data;
using CSharpDocs.Storage;
using CSharpDocs.Web;

namespace CSharpDocs.Tests;

public class RenderTests
{
    [Fact]
    public void Type_Page_Has_Expected_Sections()
    {
        var model = TestModel.LoadOrSkip();
        var type = model.Types.First(t => t.Parent is not null && t.Members.Count > 0);
        var html = Pages.TypePage(type, "", []);

        Assert.Contains(type.Name, html);
        Assert.Contains("Наследование", html);
        Assert.Contains(type.Parent!.Name, html);
    }

    [Fact]
    public void Member_Page_Renders_Signature()
    {
        var model = TestModel.LoadOrSkip();
        var type = model.Types.First(t => t.Members.Any(m => m.Kind == MemberKind.Method));
        var member = type.Members.First(m => m.Kind == MemberKind.Method);
        var html = Pages.MemberPage(type, member, "", []);
        Assert.Contains("signature", html);
        Assert.Contains(member.Name, html);
    }

    [Fact]
    public void Extension_Member_Page_Shows_This()
    {
        var model = TestModel.LoadOrSkip();
        var member = model.Types.SelectMany(t => t.Extensions).FirstOrDefault();
        if (member is null)
            Assert.Skip("в сэмпле нет методов-расширений");

        var html = Pages.MemberPage(member.Owner, member, "", []);
        Assert.Contains("this", html);
    }

    [Fact]
    public void Tree_Json_Contains_Types_With_Slugs()
    {
        var model = TestModel.LoadOrSkip();
        var json = ApiJson.BuildTree(model);
        Assert.StartsWith("[", json);
        Assert.Contains("\"slug\"", json);
        // есть хотя бы одно вложенное дерево
        Assert.Contains("\"children\"", json);
    }

    [Fact]
    public void Home_Page_And_All_Comments()
    {
        var model = TestModel.LoadOrSkip();
        Assert.Contains("Все комментарии", Pages.HomePage(model));

        var comment = new Comment { Author = "Иван", Body = "текст", TargetTitle = "IPart", TargetUrl = "/type/ipart", CreatedAt = DateTime.UtcNow };
        var all = Pages.AllCommentsPage([comment]);
        Assert.Contains("Иван", all);
        Assert.Contains("текст", all);
        // на общей странице у комментария есть ссылка на исходную страницу
        Assert.Contains("href=\"/type/ipart\"", all);
        Assert.Contains("IPart", all);
    }

    [Fact]
    public void Comment_Body_Is_Rendered_As_Markdown()
    {
        var model = TestModel.LoadOrSkip();
        var type = model.Types[0];
        var comment = new Comment
        {
            Author = "Иван",
            Body = "**важно**",
            TargetTitle = type.Name,
            TargetUrl = "/type/" + type.Slug,
            CreatedAt = DateTime.UtcNow,
        };
        var html = Pages.TypePage(type, "", [comment]);
        Assert.Contains("<strong>важно</strong>", html);
    }

    [Fact]
    public void Comment_Store_Roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"docs-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new CommentStore(path);
            store.Add(new Comment { Target = "t", TargetTitle = "T", TargetUrl = "/t", Author = "A", Body = "B" });
            Assert.Single(store.ForTarget("t"));
            Assert.Single(store.All());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
