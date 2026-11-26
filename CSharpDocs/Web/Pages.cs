using CSharpDocs.Data;
using CSharpDocs.Storage;

using static CSharpDocs.Web.Html;

namespace CSharpDocs.Web;

/// <summary>Рендеринг HTML-страниц документации.</summary>
public static class Pages
{
    public static string HomePage(ApiModel model)
    {
        var meta = model.Meta;
        var packageRows = meta.HasPackage
            ? $"""
                <dt>Пакет</dt><dd>{Esc(meta.PackageId)}{(meta.PackageVersion.Length > 0 ? " " + Esc(meta.PackageVersion) : "")}</dd>
                {(meta.Authors.Length > 0 ? $"<dt>Авторы</dt><dd>{Esc(meta.Authors)}</dd>" : "")}
                {(meta.PackageDescription.Length > 0 ? $"<dt>Описание</dt><dd>{Esc(meta.PackageDescription)}</dd>" : "")}
              """
            : "";
        var tfm = meta.TargetFramework.Length > 0 ? $"<dt>Платформа</dt><dd>{Esc(meta.TargetFramework)}</dd>" : "";

        return $"""
            <h1>{Esc(model.Title)}</h1>
            <p class="muted">Документация API. Выберите тип в дереве слева или воспользуйтесь поиском.</p>
            <h2>Сборка</h2>
            <div class="dl">
              {(meta.AssemblyName.Length > 0 ? $"<dt>Сборка</dt><dd>{Esc(meta.AssemblyName)}</dd>" : "")}
              {(meta.AssemblyVersion.Length > 0 ? $"<dt>Версия сборки</dt><dd>{Esc(meta.AssemblyVersion)}</dd>" : "")}
              {tfm}
              <dt>Типов</dt><dd>{model.Types.Count}</dd>
              <dt>Членов</dt><dd>{model.Types.Sum(t => t.Members.Count)}</dd>
              <dt>Размер XML</dt><dd>{FormatSize(meta.XmlSize)}</dd>
              <dt>Размер DLL</dt><dd>{FormatSize(meta.DllSize)}</dd>
            </div>
            {(meta.HasPackage ? $"<h2>Пакет</h2><div class=\"dl\">{packageRows}</div>" : "")}
            <p class="muted">{Link("/all", "Все комментарии")}</p>
            """;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.##} МБ",
        >= 1024 => $"{bytes / 1024.0:0.#} КБ",
        _ => $"{bytes} Б",
    };

    public static string TypePage(ApiType type, string author, IReadOnlyList<Comment> comments, bool canManage = false)
    {
        var kindBadge = $"""<span class="kindbadge">{Esc(type.KindLabel)}</span>""";
        var inheritance = type.Parent is null
            ? ""
            : $"""<h2>Наследование</h2><div class="signature">class {Esc(type.Name)} : {Link("/type/" + type.Parent.Slug, type.Parent.Name)}</div>""";
        var summary = Wrap("summary", type.Summary);
        var remarks = WrapSection("Примечания", type.Remarks);
        var children = ChildrenSection(type);
        var commentsHtml = Comments(type.Slug, author, comments, canManage);

        return $"""
            {Breadcrumbs(type)}
            <h1>{Esc(type.Name)} {kindBadge}</h1>
            <div class="muted monospace">{Esc(type.FullName)}</div>
            {summary}
            {inheritance}
            {remarks}
            {MemberTable("Конструкторы", type.Members.Where(m => m.IsConstructor))}
            {MemberTable("Значения", type.Members.Where(m => m.Kind == MemberKind.Field))}
            {MemberTable("Свойства", type.Members.Where(m => m.Kind == MemberKind.Property))}
            {MemberTable("Методы", type.Members.Where(m => m.Kind == MemberKind.Method && !m.IsConstructor))}
            {MemberTable("События", type.Members.Where(m => m.Kind == MemberKind.Event))}
            {MemberTable("Методы-расширения", type.Extensions)}
            {children}
            {commentsHtml}
            """;
    }

    public static string MemberPage(ApiType owner, ApiMember member, string author, IReadOnlyList<Comment> comments, ApiModel? model = null, bool canManage = false)
    {
        var kindBadge = $"""<span class="kindbadge">{Esc(KindLabel(member))}</span>""";
        var crumbs = $"""<div class="crumbs">{Link("/type/" + owner.Slug, owner.Name)} / <span>{Esc(member.Name)}</span></div>""";
        var summary = Wrap("summary", member.Summary);
        var returns = WrapSection("Возвращаемое значение", member.Returns);
        var remarks = WrapSection("Примечания", member.Remarks);
        var commentsHtml = Comments(owner.Slug + "/" + member.Slug, author, comments, canManage);

        return $"""
            {crumbs}
            <h1>{Esc(member.Name)} {kindBadge}</h1>
            <div class="signature">{(model is null ? Highlight(member.Signature) : Highlight(member.Signature, model))}</div>
            {summary}
            {DescriptionList("Параметры", member.ParamDocs.Select(p => (p.Name, p.Summary)))}
            {returns}
            {DescriptionList("Исключения", member.Exceptions.Select(e => (e.Cref, e.Summary)))}
            {remarks}
            {commentsHtml}
            """;
    }

    public static string AllCommentsPage(IReadOnlyList<Comment> list, ApiModel model, bool canManage = false)
    {
        var body = list.Count == 0
            ? "<p>Комментариев пока нет.</p>"
            : string.Concat(list.Select(comment => CommentBlockWithTarget(comment, model, canManage)));

        return $"""
            <h1>Все комментарии</h1>
            <p class="muted">Всего: {list.Count}</p>
            <div class="allcomments">{body}</div>
            """;
    }

    // --- Части страниц ---

    private static string Comments(string target, string author, IReadOnlyList<Comment> list, bool canManage = false)
    {
        var body = list.Count == 0
            ? """<p class="muted">Пока нет комментариев.</p>"""
            : string.Concat(list.Select(comment => CommentBlock(comment, canManage)));

        return $"""
            <div class="comments" id="comments">
            <h2>Комментарии</h2>
            {body}
            <form class="comment-form" method="post" action="/comment">
              <input type="hidden" name="target" value="{Esc(target)}">
              <input type="text" name="author" placeholder="Ваше имя" value="{Esc(author)}" required>
              <textarea name="body" rows="4" placeholder="Комментарий..." required></textarea>
              <button type="submit">Отправить</button>
            </form>
            </div>
            """;
    }

    private static string CommentBlock(Comment comment, bool canManage = false) => $"""
        <div class="comment">
          <div class="meta">{Esc(comment.Author)} · {Esc(comment.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))}</div>
          <div class="body md">{Markdown.ToHtml(comment.Body)}</div>
          {(canManage ? CommentControls(comment) : "")}
        </div>
        """;

    private static string CommentControls(Comment comment) => $"""
        <div class="comment-manage">
          <button type="button" class="comment-edit-toggle" data-id="{comment.Id}" data-body="{Esc(comment.Body)}" aria-label="Редактировать комментарий" title="Редактировать">✎</button>
          <form method="post" action="/comment/{comment.Id}/delete">
            <input type="hidden" name="expected" value="{Esc(comment.Body)}">
            <input type="hidden" name="key">
            <input type="hidden" name="returnUrl">
            <button type="submit" class="comment-delete" aria-label="Удалить комментарий" title="Удалить" onclick="return confirm('Удалить комментарий без возможности восстановления?')">×</button>
          </form>
        </div>
        """;

    /// <summary>Блок комментария со ссылкой на страницу, к которой он оставлен.</summary>
    private static string CommentBlockWithTarget(Comment comment, ApiModel model, bool canManage = false)
    {
        var target = comment.Target.Split('/', 2);
        if (!model.BySlug.TryGetValue(target[0], out var type))
            return CommentBlock(comment, canManage);

        var targetUrl = "/type/" + type.Slug;
        var targetTitle = type.FullName;
        if (target.Length == 2)
        {
            var member = type.Members.Concat(type.Extensions).FirstOrDefault(item => item.Slug == target[1]);
            if (member is null)
                return CommentBlock(comment, canManage);
            targetUrl += "/" + member.Slug;
            targetTitle += "." + member.Name;
        }

        return $"""
            <div class="comment">
              <div class="meta">{Esc(comment.Author)} · {Esc(comment.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))} · {Link(targetUrl, targetTitle)}</div>
              <div class="body md">{Markdown.ToHtml(comment.Body)}</div>
              {(canManage ? CommentControls(comment) : "")}
            </div>
            """;
    }

    private static string Breadcrumbs(ApiType type)
    {
        var chain = new List<ApiType>();
        for (var current = type; current is not null; current = current.Parent)
            chain.Add(current);
        chain.Reverse();

        var links = string.Concat(chain.Select(crumb => Link("/type/" + crumb.Slug, crumb.Name) + " / "));
        return $"""<div class="crumbs">{links}<span>{Esc(type.Name)}</span></div>""";
    }

    private static string MemberTable(string title, IEnumerable<ApiMember> members)
    {
        var list = members.ToList();
        if (list.Count == 0)
            return "";

        var rows = string.Concat(list.Select(member => $"""
            <tr>
              <td class="name">{Link($"/type/{member.Owner.Slug}/{member.Slug}", member.Display())}</td>
              <td class="desc">{Esc(ModelBuilder.Plain(member.Summary))}</td>
            </tr>
            """));

        return $"""<h2>{Esc(title)}</h2><table class="members">{rows}</table>""";
    }

    private static string DescriptionList(string title, IEnumerable<(string Name, string Summary)> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return "";

        var rows = string.Concat(list.Select(item =>
            $"""<dt><code>{Esc(item.Name)}</code></dt><dd>{item.Summary}</dd>"""));

        return $"""<h2>{Esc(title)}</h2><div class="dl">{rows}</div>""";
    }

    private static string ChildrenSection(ApiType type)
    {
        if (type.Children.Count == 0)
            return "";

        var rows = string.Concat(type.Children.Select(child => $"""
            <tr>
              <td class="name">{Link("/type/" + child.Slug, child.Name)}</td>
              <td class="desc">{Esc(ModelBuilder.Plain(child.Summary))}</td>
            </tr>
            """));

        return $"""<h2>Наследники</h2><table class="members">{rows}</table>""";
    }

    /// <summary>Оборачивает непустой HTML в div с классом.</summary>
    private static string Wrap(string cssClass, string content) =>
        content.Length > 0 ? $"""<div class="{cssClass}">{content}</div>""" : "";

    /// <summary>Секция с заголовком из непустого HTML.</summary>
    private static string WrapSection(string heading, string content) =>
        content.Length > 0 ? $"""<h2>{Esc(heading)}</h2>{content}""" : "";

    private static string KindLabel(ApiMember member) => member.Kind switch
    {
        MemberKind.Method => member.IsExtension ? "extension" : "метод",
        MemberKind.Property => "свойство",
        MemberKind.Field => "поле",
        MemberKind.Event => "событие",
        _ => "член",
    };
}
