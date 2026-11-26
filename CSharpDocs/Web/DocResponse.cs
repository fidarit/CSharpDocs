namespace CSharpDocs.Web;

/// <summary>Ответ сервера, не зависящий от инфраструктуры ASP.NET (удобно тестировать).</summary>
public sealed record DocResponse(int Status, string ContentType, byte[] Body)
{
    /// <summary>Кодирование тела ответа (например, gzip), иначе пусто.</summary>
    public string? ContentEncoding { get; init; }

    public static DocResponse Html(string html) =>
        new(200, "text/html; charset=utf-8", System.Text.Encoding.UTF8.GetBytes(html));

    public static DocResponse File(string contentType, byte[] body) => new(200, contentType, body);

    public static DocResponse Redirect(string location) =>
        new(303, "text/plain; charset=utf-8", System.Text.Encoding.UTF8.GetBytes(location));

    public static readonly DocResponse NotFound = new(404, "text/plain; charset=utf-8", []);
}

/// <summary>Данные формы добавления комментария.</summary>
public sealed record CommentForm(string Target, string Author, string Body);
