using System.IO.Compression;
using System.Text;
using CSharpDocs.Data;
using CSharpDocs.Storage;

namespace CSharpDocs.Web;

/// <summary>Логика сервера документации (без привязки к ASP.NET).</summary>
public sealed class DocsServer
{
    private const int PageSize = 40;
    private const int SearchResultLimit = 10;

    private readonly ApiModel _model;
    private readonly CommentStore _store;
    private readonly Dictionary<string, byte[]> _staticFiles;
    private readonly bool _mcpReadOnly;

    // Дерево неизменно на весь запуск — храним сжатую копию.
    private readonly byte[] _treeGzip;

    public DocsServer(ApiModel model, CommentStore store, Dictionary<string, byte[]> staticFiles, bool mcpReadOnly = false)
    {
        _model = model;
        _store = store;
        _staticFiles = staticFiles;
        _mcpReadOnly = mcpReadOnly;

        _treeGzip = BuildCompressedTree(model);
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
        return Render(t.Name + "." + m.Name, Pages.MemberPage(t, m, "", _store.ForTarget(t.Slug + "/" + m.Slug), _model), fragment);
    }

    public DocResponse AllComments(bool fragment = false) => Render("Все комментарии", Pages.AllCommentsPage(_store.All()), fragment);

    /// <summary>JSON со всей структурой дерева типов для клиента (gzip при поддержке).</summary>
    public DocResponse ApiTree(bool acceptGzip = false) => acceptGzip
        ? new DocResponse(200, "application/json; charset=utf-8", _treeGzip) { ContentEncoding = "gzip" }
        : new DocResponse(200, "application/json; charset=utf-8", Gunzip(_treeGzip));

    /// <summary>JSON-объект «slug → число комментариев» для индикации в дереве.</summary>
    public DocResponse ApiComments()
    {
        var json = ApiJson.BuildCounts(_store.Counts());
        return new DocResponse(200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
    }

    public string SearchDocumentation(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Please provide a search query.";

        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var results = new List<SearchHit>();
        foreach (var type in _model.Types)
        {
            var typeScore = MatchScore(terms, (type.Name, 1000), (type.FullName, 900), (type.Summary, 250), (type.Remarks, 150));
            if (typeScore > 0)
                results.Add(new SearchHit(typeScore, $"Type: {type.FullName} [{type.Slug}]\n{Plain(type.Summary)}"));

            foreach (var member in type.Members)
            {
                var memberScore = MatchScore(terms,
                    (member.Name, 800),
                    (member.FullName, 700),
                    (member.Signature, 300),
                    (member.Summary, 200),
                    (member.Remarks, 120),
                    (member.Returns, 100));
                if (memberScore > 0)
                    results.Add(new SearchHit(memberScore,
                        $"{member.Kind}: {member.FullName} [{type.Slug}/{member.Slug}]\n{Plain(member.Summary)}\nSignature: {member.Signature}"));
            }
        }

        foreach (var comment in _store.All())
        {
            var commentScore = MatchScore(terms, (comment.TargetTitle, 250), (comment.Body, 150), (comment.Author, 25));
            if (commentScore > 0)
                results.Add(new SearchHit(commentScore,
                    $"Comment: {comment.TargetTitle} [{comment.Target}]\n{comment.Author}: {Limit(comment.Body, 400)}"));
        }

        return results.Count == 0
            ? "No results found."
            : string.Join("\n\n", results
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Text, StringComparer.Ordinal)
                .Take(SearchResultLimit)
                .Select(result => result.Text));
    }

    private static int MatchScore(string[] terms, params (string Text, int Weight)[] fields)
    {
        var score = 0;
        foreach (var (text, weight) in fields)
        {
            if (text.Length == 0)
                continue;

            var fieldScore = weight;
            foreach (var term in terms)
            {
                var comparison = StringComparison.OrdinalIgnoreCase;
                var match = string.Equals(text, term, comparison) ? 300
                    : text.StartsWith(term, comparison) ? 200
                    : text.Contains(term, comparison) ? 100
                    : 0;
                if (match == 0)
                {
                    fieldScore = 0;
                    break;
                }
                fieldScore += match;
            }

            score = Math.Max(score, fieldScore);
        }

        return score;
    }

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private sealed record SearchHit(int Score, string Text);

    public string GetDocumentation(string slug)
    {
        var split = slug.Split('/', 2);
        if (!_model.BySlug.TryGetValue(split[0], out var type))
            return "Documentation item not found.";

        if (split.Length == 1)
        {
            var text = new StringBuilder()
                .AppendLine($"{type.KindLabel} {type.FullName}")
                .AppendLine(type.Summary.Length > 0 ? Plain(type.Summary) : "")
                .AppendLine(type.Remarks.Length > 0 ? "Remarks: " + Plain(type.Remarks) : "");
            if (type.Parent is not null)
                text.AppendLine("Base type: " + type.Parent.FullName);
            foreach (var typeMember in type.Members.Concat(type.Extensions))
                text.AppendLine($"{typeMember.Kind}: {typeMember.Signature} — {Plain(typeMember.Summary)} [{type.Slug}/{typeMember.Slug}]");
            AppendComments(text, type.Slug);
            return text.ToString();
        }

        var member = type.Members.Concat(type.Extensions).FirstOrDefault(m => m.Slug == split[1]);
        if (member is null)
            return "Documentation item not found.";

        var result = new StringBuilder()
            .AppendLine($"{member.Kind}: {member.FullName}")
            .AppendLine("Signature: " + member.Signature)
            .AppendLine(Plain(member.Summary))
            .AppendLine(member.Remarks.Length > 0 ? "Remarks: " + Plain(member.Remarks) : "")
            .AppendLine(member.Returns.Length > 0 ? "Returns: " + Plain(member.Returns) : "");
        foreach (var param in member.ParamDocs)
            result.AppendLine($"Parameter {param.Name}: {Plain(param.Summary)}");
        AppendComments(result, type.Slug + "/" + member.Slug);
        return result.ToString();
    }

    public string GetComments(string slug)
    {
        if (!TryResolveTarget(slug, out var target, out _, out _))
            return "Documentation item not found.";
        var comments = _store.ForTarget(target);
        return comments.Count == 0
            ? "There are no comments yet."
            : string.Join("\n\n", comments.Select(c => $"{c.Author} ({c.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}):\n{c.Body}"));
    }

    public string AddMcpComment(string slug, string author, string body)
    {
        if (_mcpReadOnly)
            return "Adding comments is disabled because MCP is in read-only mode.";
        if (!TryResolveTarget(slug, out var target, out var title, out var url))
            return "Documentation item not found.";
        if (string.IsNullOrWhiteSpace(body))
            return "Comment cannot be empty.";
        if (body.Length > 10000 || author.Length > 200)
            return "Comment exceeds the maximum allowed length.";

        _store.Add(new Comment { Target = target, TargetTitle = title, TargetUrl = url, Author = author.Trim(), Body = body.Trim() });
        return "Comment added to " + title + ".";
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
        if (comments.Count == 0)
            return;

        text.AppendLine("Comments:");
        foreach (var comment in comments)
            text.AppendLine($"- {comment.Author} ({comment.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}): {comment.Body}");
    }

    private static string Plain(string html) => Data.ModelBuilder.Plain(html);

    private static byte[] BuildCompressedTree(ApiModel model)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            ApiJson.WriteTree(gzip, model);
        return output.ToArray();
    }

    private static byte[] Gunzip(byte[] data)
    {
        var outputLength = BitConverter.ToUInt32(data, data.Length - sizeof(uint));
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        var output = new byte[outputLength];
        var offset = 0;
        while (offset < output.Length)
        {
            var read = gzip.Read(output, offset, output.Length - offset);
            if (read == 0)
                throw new InvalidDataException("Поврежденный gzip-кэш дерева API");
            offset += read;
        }
        return output;
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
