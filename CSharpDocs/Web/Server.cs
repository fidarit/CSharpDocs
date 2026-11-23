using System.IO.Compression;
using System.Text;
using CSharpDocs.Data;
using CSharpDocs.Storage;

namespace CSharpDocs.Web;

/// <summary>Логика сервера документации (без привязки к ASP.NET).</summary>
public sealed class DocsServer
{
    private const int PageSize = 40;

    private readonly ApiModel _model;
    private readonly CommentStore _store;
    private readonly Dictionary<string, byte[]> _staticFiles;
    private readonly bool _mcpReadOnly;

    // Дерево неизменно на весь запуск — сжимаем один раз и отдаём готовые байты.
    private readonly byte[] _treePlain;
    private readonly byte[] _treeGzip;

    public DocsServer(ApiModel model, CommentStore store, Dictionary<string, byte[]> staticFiles, bool mcpReadOnly = false)
    {
        _model = model;
        _store = store;
        _staticFiles = staticFiles;
        _mcpReadOnly = mcpReadOnly;

        _treePlain = Encoding.UTF8.GetBytes(ApiJson.BuildTree(model));
        _treeGzip = Gzip(_treePlain);
    }

    public DocResponse Home(bool fragment = false) => Render("", HomePage(), fragment);

    public DocResponse TypePage(string slug, bool fragment = false)
    {
        if (!_model.BySlug.TryGetValue(slug, out var t))
            return DocResponse.NotFound;
        return Render(t.Name, Pages.TypePage(t, "", _store.ForTarget(t.Slug)), fragment);
    }

    public DocResponse MemberPage(string slug, string member, bool fragment = false)
    {
        if (!_model.BySlug.TryGetValue(slug, out var t))
            return DocResponse.NotFound;
        var m = t.Members.FirstOrDefault(x => x.Slug == member)
            ?? t.Extensions.FirstOrDefault(x => x.Slug == member);
        if (m is null)
            return DocResponse.NotFound;
        return Render(t.Name + "." + m.Name, Pages.MemberPage(t, m, "", _store.ForTarget(t.Slug + "/" + m.Slug)), fragment);
    }

    public DocResponse AllComments(bool fragment = false) => Render("Все комментарии", Pages.AllCommentsPage(_store.All()), fragment);

    /// <summary>JSON со всей структурой дерева типов для клиента (gzip при поддержке).</summary>
    public DocResponse ApiTree(bool acceptGzip = false) =>
        acceptGzip
            ? new DocResponse(200, "application/json; charset=utf-8", _treeGzip) { ContentEncoding = "gzip" }
            : new DocResponse(200, "application/json; charset=utf-8", _treePlain);

    /// <summary>JSON-объект «slug → число комментариев» для индикации в дереве.</summary>
    public DocResponse ApiComments()
    {
        var json = ApiJson.BuildCounts(_store.Counts());
        return new DocResponse(200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
    }

    public string SearchDocumentation(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Укажите поисковый запрос.";

        var needle = query.Trim();
        var results = new List<string>();
        foreach (var type in _model.Types)
        {
            var typeText = Join(type.FullName, type.Summary, type.Remarks);
            if (Contains(typeText, needle))
                results.Add($"Тип: {type.FullName} [{type.Slug}]\n{Plain(type.Summary)}");

            foreach (var member in type.Members.Concat(type.Extensions))
            {
                var memberText = Join(member.FullName, member.Signature, member.Summary, member.Remarks, member.Returns);
                if (Contains(memberText, needle))
                    results.Add($"{member.Kind}: {member.FullName} [{type.Slug}/{member.Slug}]\n{Plain(member.Summary)}\nСигнатура: {member.Signature}");
            }
        }

        foreach (var comment in _store.All())
        {
            if (!Contains(Join(comment.TargetTitle, comment.Body), needle))
                continue;
            results.Add($"Комментарий: {comment.TargetTitle} [{comment.Target}]\n{comment.Author}: {comment.Body}");
        }

        return results.Count == 0
            ? "Ничего не найдено."
            : string.Join("\n\n", results.Take(50));
    }

    public string GetDocumentation(string slug)
    {
        var split = slug.Split('/', 2);
        if (!_model.BySlug.TryGetValue(split[0], out var type))
            return "Элемент документации не найден.";

        if (split.Length == 1)
        {
            var text = new StringBuilder()
                .AppendLine($"{type.KindLabel} {type.FullName}")
                .AppendLine(type.Summary.Length > 0 ? Plain(type.Summary) : "")
                .AppendLine(type.Remarks.Length > 0 ? "Примечания: " + Plain(type.Remarks) : "");
            if (type.Parent is not null)
                text.AppendLine("Базовый тип: " + type.Parent.FullName);
            foreach (var typeMember in type.Members.Concat(type.Extensions))
                text.AppendLine($"{typeMember.Kind}: {typeMember.Signature} — {Plain(typeMember.Summary)} [{type.Slug}/{typeMember.Slug}]");
            AppendComments(text, type.Slug);
            return text.ToString();
        }

        var member = type.Members.Concat(type.Extensions).FirstOrDefault(m => m.Slug == split[1]);
        if (member is null)
            return "Элемент документации не найден.";

        var result = new StringBuilder()
            .AppendLine($"{member.Kind}: {member.FullName}")
            .AppendLine("Сигнатура: " + member.Signature)
            .AppendLine(Plain(member.Summary))
            .AppendLine(member.Remarks.Length > 0 ? "Примечания: " + Plain(member.Remarks) : "")
            .AppendLine(member.Returns.Length > 0 ? "Возвращаемое значение: " + Plain(member.Returns) : "");
        foreach (var param in member.ParamDocs)
            result.AppendLine($"Параметр {param.Name}: {Plain(param.Summary)}");
        AppendComments(result, type.Slug + "/" + member.Slug);
        return result.ToString();
    }

    public string GetComments(string slug)
    {
        if (!TryResolveTarget(slug, out var target, out _, out _))
            return "Элемент документации не найден.";
        var comments = _store.ForTarget(target);
        return comments.Count == 0
            ? "Комментариев пока нет."
            : string.Join("\n\n", comments.Select(c => $"{c.Author} ({c.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}):\n{c.Body}"));
    }

    public string AddMcpComment(string slug, string author, string body)
    {
        if (_mcpReadOnly)
            return "Добавление комментариев отключено: MCP работает в режиме только для чтения.";
        if (!TryResolveTarget(slug, out var target, out var title, out var url))
            return "Элемент документации не найден.";
        if (string.IsNullOrWhiteSpace(body))
            return "Комментарий не может быть пустым.";
        if (body.Length > 10000 || author.Length > 200)
            return "Комментарий превышает допустимый размер.";

        _store.Add(new Comment { Target = target, TargetTitle = title, TargetUrl = url, Author = author.Trim(), Body = body.Trim() });
        return "Комментарий добавлен к " + title + ".";
    }

    private bool TryResolveTarget(string slug, out string target, out string title, out string url)
    {
        target = title = url = "";
        var split = slug.Split('/', 2);
        if (!_model.BySlug.TryGetValue(split[0], out var type))
            return false;
        if (split.Length == 1)
        {
            target = type.Slug;
            title = type.FullName;
            url = "/type/" + type.Slug;
            return true;
        }

        var member = type.Members.Concat(type.Extensions).FirstOrDefault(m => m.Slug == split[1]);
        if (member is null)
            return false;
        target = type.Slug + "/" + member.Slug;
        title = type.FullName + "." + member.Name;
        url = "/type/" + type.Slug + "/" + member.Slug;
        return true;
    }

    private void AppendComments(StringBuilder text, string target)
    {
        var comments = _store.ForTarget(target);
        text.AppendLine("Комментарии:");
        if (comments.Count == 0)
            text.AppendLine("Пока нет комментариев.");
        foreach (var comment in comments)
            text.AppendLine($"- {comment.Author} ({comment.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}): {comment.Body}");
    }

    private static string Join(params string[] values) => string.Join(' ', values.Where(v => v.Length > 0).Select(Plain));
    private static string Plain(string html) => Data.ModelBuilder.Plain(html);
    private static bool Contains(string text, string query) => text.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(data);
        return output.ToArray();
    }

    public DocResponse AddComment(CommentForm form)
    {
        _store.Add(new Comment
        {
            Target = form.Target,
            TargetTitle = form.TargetTitle,
            TargetUrl = form.TargetUrl,
            Author = form.Author,
            Body = form.Body,
        });
        return DocResponse.Redirect(form.TargetUrl.Length == 0 ? "/" : form.TargetUrl + "#comments");
    }

    public DocResponse Static(string file)
    {
        if (file.Contains("..", StringComparison.Ordinal) || file.Contains('/'))
            return DocResponse.NotFound;
        return _staticFiles.TryGetValue(file, out var content)
            ? DocResponse.File(ContentType(file), content)
            : DocResponse.NotFound;
    }

    private string HomePage() => Pages.HomePage(_model);

    private DocResponse Render(string pageTitle, string body, bool fragment)
    {
        if (fragment)
            return DocResponse.Html(body);

        var chrome = new Chrome(
            Brand: _model.Title,
            PageTitle: pageTitle,
            Source: _model.Meta.SiteName.Length > 0 ? _model.Meta.SiteName : _model.Version,
            TargetFramework: _model.Meta.TargetFramework);
        return DocResponse.Html(Layout.Page(chrome, "", body));
    }

    private static string ContentType(string file) => file switch
    {
        _ when file.EndsWith(".css", StringComparison.Ordinal) => "text/css; charset=utf-8",
        _ when file.EndsWith(".js", StringComparison.Ordinal) => "text/javascript; charset=utf-8",
        _ => "application/octet-stream",
    };

    /// <summary>Регистрирует маршруты в приложении ASP.NET.</summary>
    public void Map(WebApplication app)
    {
        app.MapGet("/", (HttpRequest r) => ToResult(Home(IsFragment(r))));
        app.MapGet("/type/{slug}", (HttpRequest r, string slug) => ToResult(TypePage(slug, IsFragment(r))));
        app.MapGet("/type/{slug}/{member}", (HttpRequest r, string slug, string member) => ToResult(MemberPage(slug, member, IsFragment(r))));
        app.MapGet("/api/tree", (HttpRequest r) => ToResult(ApiTree(AcceptsGzip(r))));
        app.MapGet("/api/comments", () => ToResult(ApiComments()));
        app.MapGet("/all", (HttpRequest r) => ToResult(AllComments(IsFragment(r))));
        app.MapGet("/static/{file}", (string file) => ToResult(Static(file)));
        app.MapPost("/comment", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return ToResult(AddComment(new CommentForm(
                form["target"].ToString(),
                form["target_title"].ToString(),
                form["target_url"].ToString(),
                form["author"].ToString(),
                form["body"].ToString())));
        });
    }

    /// <summary>Клиент готов принять gzip.</summary>
    private static bool AcceptsGzip(HttpRequest request) =>
        request.Headers.AcceptEncoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase);

    /// <summary>Запрос от AJAX-навигации: нужен только фрагмент без каркаса страницы.</summary>
    private static bool IsFragment(HttpRequest request) =>
        request.Headers["X-Requested-With"] == "fetch";

    private static IResult ToResult(DocResponse response) => response.Status switch
    {
        404 => Results.NotFound(),
        303 => Results.Redirect(Encoding.UTF8.GetString(response.Body)),
        _ => Bytes(response),
    };

    private static IResult Bytes(DocResponse response)
    {
        var bytes = Results.Bytes(response.Body, response.ContentType);
        return response.ContentEncoding is null ? bytes : new EncodedResult(bytes, response.ContentEncoding);
    }

    /// <summary>Обёртка, добавляющая Content-Encoding к ответу с байтами.</summary>
    private sealed class EncodedResult(IResult inner, string encoding) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.ContentEncoding = encoding;
            return inner.ExecuteAsync(httpContext);
        }
    }
}
