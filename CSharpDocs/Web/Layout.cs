using CSharpDocs.Data;

using static CSharpDocs.Web.Html;

namespace CSharpDocs.Web;

/// <summary>Данные шапки страницы (сайдбар и title).</summary>
/// <param name="Brand">Заголовок сайта (кликабелен на главную).</param>
/// <param name="PageTitle">Заголовок конкретной страницы для тега title.</param>
/// <param name="Source">Имя источника: пакет/сборка с версией.</param>
/// <param name="TargetFramework">Целевой фреймворк (может быть пустым).</param>
public sealed record Chrome(string Brand, string PageTitle, string Source, string TargetFramework);

/// <summary>
/// Каркас страницы: шапка сайдбара, пустой контейнер дерева и область контента.
/// Дерево строится на клиенте из JSON (/api/tree).
/// </summary>
public static class Layout
{
    public static string Page(Chrome chrome, string query, string body)
    {
        var brand = Esc(chrome.Brand);
        var source = Esc(chrome.Source);
        var tfm = chrome.TargetFramework.Length > 0
            ? $"""<span class="meta-tfm">{Esc(chrome.TargetFramework)}</span>"""
            : "";
        var title = chrome.PageTitle.Length > 0
            ? $"{chrome.PageTitle} — {chrome.Source}"
            : chrome.Source;

        return $"""
            <!doctype html>
            <html lang="ru">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{Esc(title)}</title>
              <script src="/static/theme.js"></script>
              <link rel="stylesheet" href="/static/style.css">
              <link rel="stylesheet" href="/static/highlight.css">
            </head>
            <body>
            <div class="layout">
              <aside class="sidebar">
                <div class="sidebar-top">
                  <div class="brand">
                    <a class="brand-link" href="/">{brand}</a>
                    <button type="button" id="theme-toggle" class="theme-toggle" title="Тема: системная" aria-label="Переключить тему"></button>
                  </div>
                  <div class="brand-meta">{source}{tfm}</div>
                  <div class="searchbox">
                    <input type="search" name="q" id="q" placeholder="Поиск: имя, * и ? ..." value="{Esc(query)}" autocomplete="off" autofocus>
                    <button type="button" id="q-clear" class="search-clear" title="Очистить" hidden>&times;</button>
                  </div>
                  <div class="search-hint" id="search-hint">Шаблоны: <code>*</code> — любая подстрока, <code>?</code> — один символ.</div>
                </div>
                <nav class="tree" id="tree"></nav>
              </aside>
              <main class="main" id="content">
            {body}
              </main>
            </div>
            <script src="/static/app.js"></script>
            </body>
            </html>
            """;
    }
}
