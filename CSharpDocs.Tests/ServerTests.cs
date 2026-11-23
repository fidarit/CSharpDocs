using System.Text;
using CSharpDocs.Data;
using CSharpDocs.Storage;
using CSharpDocs.Web;

namespace CSharpDocs.Tests;

public class ServerTests
{
    private static (DocsServer Server, ApiModel Model, string DbPath) Create()
    {
        var model = TestModel.LoadOrSkip();
        var dbPath = Path.Combine(Path.GetTempPath(), $"docs-{Guid.NewGuid():N}.db");
        var store = new CommentStore(dbPath);
        return (new DocsServer(model, store, []), model, dbPath);
    }

    private static string Body(DocResponse response) => Encoding.UTF8.GetString(response.Body);

    [Fact]
    public void Home_Returns_Html()
    {
        var (server, model, db) = Create();
        try
        {
            var response = server.Home();
            Assert.Equal(200, response.Status);
            Assert.Contains(model.Title, Body(response));
            Assert.Contains("Все комментарии", Body(response));
        }
        finally { File.Delete(db); }
    }

    [Fact]
    public void Type_And_Member_Pages()
    {
        var (server, model, db) = Create();
        try
        {
            var type = model.Types.First(t => t.Parent is not null && t.Members.Count > 0);
            var typePage = server.TypePage(type.Slug);
            Assert.Equal(200, typePage.Status);
            Assert.Contains("Наследование", Body(typePage));

            var member = type.Members[0];
            var memberPage = server.MemberPage(type.Slug, member.Slug);
            Assert.Equal(200, memberPage.Status);
            Assert.Contains("signature", Body(memberPage));
        }
        finally { File.Delete(db); }
    }

    [Fact]
    public void Unknown_Returns_404()
    {
        var (server, _, db) = Create();
        try
        {
            Assert.Equal(404, server.TypePage("nonexistent").Status);
        }
        finally { File.Delete(db); }
    }

    [Fact]
    public void Search_And_Tree()
    {
        var (server, model, db) = Create();
        try
        {
            // JSON-дерево отдаётся целиком и содержит типы и пространства имён.
            var tree = server.ApiTree();
            Assert.Equal(200, tree.Status);
            Assert.Equal("application/json; charset=utf-8", tree.ContentType);
            Assert.Contains("\"kind\":\"namespace\"", Body(tree));
            Assert.Contains(model.Types[0].Slug, Body(tree));

            // gzip-вариант: те же данные в сжатом виде.
            var gz = server.ApiTree(acceptGzip: true);
            Assert.Equal("gzip", gz.ContentEncoding);
            Assert.True(gz.Body.Length < tree.Body.Length);

            // AJAX-фрагмент: без каркаса страницы (нет <html>), но с контентом типа.
            var withParent = model.Types.First(t => t.Parent is not null);
            var fragment = server.TypePage(withParent.Slug, fragment: true);
            Assert.Equal(200, fragment.Status);
            Assert.DoesNotContain("<html", Body(fragment));
            Assert.Contains("Наследование", Body(fragment));

            // Полная страница: с каркасом.
            Assert.Contains("<html", Body(server.TypePage(withParent.Slug)));
        }
        finally { File.Delete(db); }
    }

    [Fact]
    public void Comment_Flow()
    {
        var (server, model, db) = Create();
        try
        {
            var type = model.Types[0];
            var redirect = server.AddComment(new CommentForm(type.Slug, type.Name, "/type/" + type.Slug, "Иван", "Проверка"));
            Assert.Equal(303, redirect.Status);
            Assert.Contains("Проверка", Body(server.TypePage(type.Slug)));
            Assert.Contains("Иван", Body(server.AllComments()));
        }
        finally { File.Delete(db); }
    }

    [Fact]
    public void Mcp_Reads_Documentation_And_Comments()
    {
        var (server, model, db) = Create();
        try
        {
            var type = model.Types[0];
            var added = server.AddMcpComment(type.Slug, "Бот", "Комментарий MCP");
            Assert.Contains("добавлен", added);
            Assert.Contains(type.FullName, server.GetDocumentation(type.Slug));
            Assert.Contains("Комментарий MCP", server.GetComments(type.Slug));
            Assert.Contains("Комментарий MCP", server.GetDocumentation(type.Slug));
            var search = server.SearchDocumentation(type.Name);
            Assert.StartsWith($"Тип: {type.FullName} [{type.Slug}]", search);
            Assert.True(search.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Length <= 10);
            Assert.Contains("Комментарий MCP", server.SearchDocumentation("Комментарий MCP"));
            Assert.Contains("не найден", server.AddMcpComment("missing", "Бот", "Текст"));
        }
        finally { File.Delete(db); }
    }

    [Fact]
    public void Mcp_Read_Only_Rejects_Comment_Writes()
    {
        var model = TestModel.LoadOrSkip();
        var db = Path.Combine(Path.GetTempPath(), $"docs-{Guid.NewGuid():N}.db");
        using var store = new CommentStore(db);
        var server = new DocsServer(model, store, [], mcpReadOnly: true);
        try
        {
            var type = model.Types[0];
            Assert.Contains("только для чтения", server.AddMcpComment(type.Slug, "Бот", "Текст"));
            Assert.Empty(store.All());
        }
        finally { File.Delete(db); }
    }

    [Fact]
    public void Static_Serves_Style()
    {
        var model = TestModel.LoadOrSkip();
        var dbPath = Path.Combine(Path.GetTempPath(), $"docs-{Guid.NewGuid():N}.db");
        using var store = new CommentStore(dbPath);
        var server = new DocsServer(model, store, new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["style.css"] = [1, 2, 3],
        });
        try
        {
            var response = server.Static("style.css");
            Assert.Equal(200, response.Status);
            Assert.Equal("text/css; charset=utf-8", response.ContentType);
            Assert.Equal(404, server.Static("../secret").Status);
        }
        finally { File.Delete(dbPath); }
    }
}
