using CSharpDocs.Storage;
using ModelContextProtocol.Server;

namespace CSharpDocs.Web;

public sealed class McpService(DocsServer docsServer, bool enabled, bool readOnly)
{
    public void Configure(WebApplicationBuilder builder)
    {
        if (!enabled)
            return;

        var tools = new List<McpServerTool>
        {
            McpServerTool.Create((string query) => docsServer.SearchDocumentation(query), new()
            {
                Name = "search_documentation",
                Description = "Ищет по словам. Точные имена типов и членов выше сигнатур, описаний и комментариев; возвращает до 10 лучших совпадений со slug для get_documentation.",
            }),
            McpServerTool.Create((string slug) => docsServer.GetDocumentation(slug), new()
            {
                Name = "get_documentation",
                Description = "Возвращает документацию элемента API и комментарии к нему.",
            }),
            McpServerTool.Create((string slug) => docsServer.GetComments(slug), new()
            {
                Name = "get_comments",
                Description = "Возвращает комментарии к элементу API.",
            }),
        };

        if (!readOnly)
            tools.Add(McpServerTool.Create((string slug, string author, string body) =>
                docsServer.AddMcpComment(slug, author, body), new()
            {
                Name = "add_comment",
                Description = "Добавляет комментарий к типу или члену API по его slug.",
            }));

        builder.Services.AddMcpServer()
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools(tools);
    }

    public void Map(WebApplication app)
    {
        if (enabled)
            app.MapMcp("/mcp");
    }
}
