using CSharpDocs.Data;
using CSharpDocs.Storage;
using CSharpDocs.Web;

namespace CSharpDocs.Tests;

public class RenderTests
{
    [Fact]
    public void Highlight_Links_Only_Unique_Short_Type_Names()
    {
        var model = new ApiModel();
        model.Types.Add(new ApiType { Name = "Widget", FullName = "First.Widget", Slug = "first-widget" });
        model.Types.Add(new ApiType { Name = "Widget", FullName = "Second.Widget", Slug = "second-widget" });
        model.Types.Add(new ApiType { Name = "Unique", FullName = "Third.Unique", Slug = "third-unique" });

        var ambiguous = Html.Highlight("Widget", model);
        var unique = Html.Highlight("Unique", model);
        var fullyQualified = Html.Highlight("First.Widget", model);

        Assert.DoesNotContain("<a ", ambiguous);
        Assert.Contains("<span class=\"hl-type\">Widget</span>", ambiguous);
        Assert.Contains("href=\"/type/third-unique\"", unique);
        Assert.Contains("href=\"/type/first-widget\"", fullyQualified);
    }

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
    public void Member_Page_Omits_Owner_Name_From_Signature()
    {
        var owner = new ApiType { Name = "Widget", FullName = "Example.Widget", Slug = "example-widget" };
        var member = new ApiMember
        {
            Owner = owner,
            Kind = MemberKind.Method,
            Name = "Run",
            Slug = "run",
            Signature = "void Widget.Run(int value)",
        };

        var html = Pages.MemberPage(owner, member, "", []);

        Assert.Contains("Run</span>(int value)", html);
        Assert.DoesNotContain("Widget</span>.Run", html);
    }

    [Fact]
    public void Member_Display_Shows_Only_Name_And_Parameter_Types()
    {
        var owner = new ApiType { Name = "Widget" };
        var member = new ApiMember
        {
            Owner = owner,
            Kind = MemberKind.Method,
            Name = "Run",
            Signature = "void Widget.Run(int value, System.String name)",
            Params = [new ParamInfo { Type = "int", Name = "value" }, new ParamInfo { Type = "string", Name = "name" }],
        };

        Assert.Equal("Run(int, string)", member.Display());
    }

    [Fact]
    public void Type_Page_Shows_Short_Field_And_Method_Links()
    {
        var type = new ApiType { Name = "Widget", FullName = "Example.Widget", Slug = "example-widget" };
        type.Members.Add(new ApiMember
        {
            Owner = type,
            Kind = MemberKind.Field,
            Name = "Value",
            Slug = "value",
            Signature = "Widget.Value",
        });
        type.Members.Add(new ApiMember
        {
            Owner = type,
            Kind = MemberKind.Method,
            Name = "Run",
            Slug = "run",
            Signature = "void Widget.Run(int value, System.String name)",
            Params = [new ParamInfo { Type = "int", Name = "value" }, new ParamInfo { Type = "string", Name = "name" }],
        });

        var html = Pages.TypePage(type, "", []);

        Assert.Contains(">Value</a>", html);
        Assert.Contains(">Run(int, string)</a>", html);
        Assert.DoesNotContain(">Widget.Value</a>", html);
        Assert.DoesNotContain(">Widget.Run", html);
    }

    [Fact]
    public void Enum_Page_Shows_Short_Value_Links()
    {
        var type = new ApiType { Name = "State", FullName = "Example.State", Slug = "example-state", Kind = TypeKind.Enum };
        type.Members.Add(new ApiMember
        {
            Owner = type,
            Kind = MemberKind.Field,
            Name = "Ready",
            Slug = "ready",
            Signature = "State.Ready",
        });

        var html = Pages.TypePage(type, "", []);

        Assert.Contains(">Ready</a>", html);
        Assert.DoesNotContain(">State.Ready</a>", html);
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

        var type = new ApiType { Name = "IPart", FullName = "Lib.IPart", Slug = "lib-ipart" };
        var modelWithType = new ApiModel();
        modelWithType.Types.Add(type);
        modelWithType.BySlug[type.Slug] = type;
        var comment = new Comment { Author = "Иван", Body = "текст", Target = type.Slug, CreatedAt = DateTime.UtcNow };
        var all = Pages.AllCommentsPage([comment], modelWithType);
        Assert.Contains("Иван", all);
        Assert.Contains("текст", all);
        Assert.Contains("href=\"/type/lib-ipart\"", all);
        Assert.Contains("Lib.IPart", all);
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
            Target = type.Slug,
            TargetTitle = type.Name,
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
            store.Add(new Comment { Target = "t", TargetTitle = "T", Author = "A", Body = "B" });
            Assert.Single(store.ForTarget("t"));
            Assert.Single(store.All());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
