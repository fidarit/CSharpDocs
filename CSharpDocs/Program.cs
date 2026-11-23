using System.Reflection;
using CSharpDocs;
using CSharpDocs.Data;
using CSharpDocs.Storage;
using CSharpDocs.Web;

// Универсальный сервер документации .NET-сборок (Native AOT).
//
// Источники: -xml (готовая документация) либо -nupkg (XML и DLL берутся из
// NuGet-пакета). Наследование, перечисления и методы-расширения читаются из
// метаданных сборки. Комментарии — в SQLite.

var options = Options.FromArgs(args);
var model = ModelBuilder.Build(
    new MemoryStream(options.Xml),
    new MemoryStream(options.Dll),
    options.Version);
model.Title = options.Title;
model.Meta.XmlSize = options.Xml.Length;
model.Meta.DllSize = options.Dll.Length;
model.Meta.PackageId = options.PackageId;
model.Meta.PackageVersion = options.PackageVersion;
model.Meta.PackageDescription = options.PackageDescription;
model.Meta.Authors = options.PackageAuthors;
Console.WriteLine($"Загружено типов: {model.Types.Count}");

var store = new CommentStore(options.DbPath);
var docsServer = new DocsServer(model, store, LoadStatic(), options.McpReadOnly);
var mcpService = new McpService(docsServer, options.McpEnabled, options.McpReadOnly);
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls(options.Urls);
builder.Services.AddSingleton(store);
mcpService.Configure(builder);

var app = builder.Build();
docsServer.Map(app);
mcpService.Map(app);

Console.WriteLine($"Документация доступна на {options.Urls}");
try
{
    app.Run();
}
finally
{
    store.Dispose();
}

static Dictionary<string, byte[]> LoadStatic()
{
    var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    var assembly = Assembly.GetExecutingAssembly();
    foreach (var name in assembly.GetManifestResourceNames())
    {
        const string marker = "Web.Static.";
        var idx = name.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            continue;
        var fileName = name[(idx + marker.Length)..];
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        result[fileName] = ms.ToArray();
    }
    return result;
}
