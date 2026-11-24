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
                Description = "Searches API names, signatures, descriptions, and user comments. Returns up to 10 best matches with slugs for get_documentation.",
            }),
            McpServerTool.Create((string slug) => docsServer.GetDocumentation(slug), new()
            {
                Name = "get_documentation",
                Description = "Returns API documentation and includes user comments when present. Omits the comments section when there are no comments.",
            }),
            McpServerTool.Create((string slug) => docsServer.GetComments(slug), new()
            {
                Name = "get_comments",
                Description = "Returns user comments for an API item, or states that there are no comments. Use only when comments are relevant to the user's request; do not call automatically after get_documentation.",
            }),
        };

        if (!readOnly)
            tools.Add(McpServerTool.Create((string slug, string author, string body) =>
                docsServer.AddMcpComment(slug, author, body), new()
            {
                Name = "add_comment",
                Description = "Adds a comment to an API type or member by slug.",
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
