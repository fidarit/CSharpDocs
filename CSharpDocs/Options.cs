using CSharpDocs.Data;

namespace CSharpDocs;

/// <summary>Параметры запуска сервера.</summary>
public sealed class Options
{
    public string Urls { get; init; } = "http://localhost:8080";
    public string DbPath { get; init; } = "comments.db";
    public string Version { get; init; } = "dev";
    public string Title { get; init; } = "API Docs";
    public bool McpEnabled { get; init; }
    public bool McpReadOnly { get; init; }
    public byte[] Xml { get; init; } = [];
    public byte[] Dll { get; init; } = [];

    public string PackageId { get; init; } = "";
    public string PackageVersion { get; init; } = "";
    public string PackageDescription { get; init; } = "";
    public string PackageAuthors { get; init; } = "";

    public static Options FromArgs(string[] args)
    {
        var map = ParseArgs(args);
        string? xml = map.GetValueOrDefault("xml");
        string? nupkg = map.GetValueOrDefault("nupkg");
        var packageId = map.GetValueOrDefault("package");
        var db = map.GetValueOrDefault("db") ?? "comments.db";
        var version = map.GetValueOrDefault("version") ?? "";
        var title = map.GetValueOrDefault("title") ?? "API Docs";
        var mcpEnabled = ReadBooleanOption(map.GetValueOrDefault("mcp"), Environment.GetEnvironmentVariable("MCP_ENABLED"));
        var mcpReadOnly = ReadBooleanOption(map.GetValueOrDefault("mcp-read-only"), Environment.GetEnvironmentVariable("MCP_READ_ONLY"));

        byte[] xmlData;
        byte[] dllData;
        string sourcePath;
        var packageMeta = new PackageMeta("", "", "", "");

        if (xml is not null)
        {
            xmlData = File.ReadAllBytes(xml);
            dllData = map.TryGetValue("dll", out var dllPath)
                ? File.ReadAllBytes(dllPath)
                : LibraryFromSibling(xml);
            sourcePath = xml;
        }
        else
        {
            var package = nupkg ?? FindPackage(packageId)
                ?? throw new InvalidOperationException(
                    "Укажите -xml/-nupkg либо переменную окружения API_PACKAGE с именем NuGet-пакета");
            var (pkgXml, pkgDll, meta) = PackageSource.FromNupkg(package);
            xmlData = pkgXml;
            dllData = pkgDll;
            packageMeta = meta;
            sourcePath = package;
            Console.WriteLine($"Источник: {package} ({meta.Id} {meta.Version})");
        }

        if (dllData.Length == 0)
            throw new InvalidOperationException("Не найдена сборка (.dll) для чтения структуры типов");

        if (version.Length == 0)
            version = DetectVersion(sourcePath);

        var urls = map.GetValueOrDefault("urls")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?? "http://localhost:8080";

        return new Options
        {
            Urls = urls,
            DbPath = db,
            Version = version,
            Title = title,
            McpEnabled = mcpEnabled,
            McpReadOnly = mcpReadOnly,
            Xml = xmlData,
            Dll = dllData,
            PackageId = packageMeta.Id,
            PackageVersion = packageMeta.Version,
            PackageDescription = packageMeta.Description,
            PackageAuthors = packageMeta.Authors,
        };
    }

    internal static bool ParseBoolean(string value) =>
        value.Length == 0 || bool.TryParse(value, out var enabled) && enabled;

    internal static bool ReadBooleanOption(string? argument, string? environmentValue) =>
        ParseBoolean(argument ?? environmentValue ?? "false");

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
                arg = arg[1..];
            if (!arg.StartsWith('-') || arg.Length < 2)
                continue;
            var key = arg.TrimStart('-');
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                result[key] = args[++i];
            else
                result[key] = "";
        }
        return result;
    }

    private static byte[] LibraryFromSibling(string xmlPath)
    {
        var dll = Path.ChangeExtension(xmlPath, ".dll");
        return File.Exists(dll) ? File.ReadAllBytes(dll) : [];
    }

    private static string? FindPackage(string? packageId)
    {
        var id = packageId ?? Environment.GetEnvironmentVariable("API_PACKAGE");
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        return PackageSource.FindLatestNupkg(cache, id);
    }

    /// <summary>
    /// Определяет версию по пути источника: сначала по имени каталога
    /// (например .../25.5.9/...), затем по имени файла (mylib.1.2.3.nupkg).
    /// </summary>
    internal static string DetectVersion(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath))
            return "dev";

        var dir = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
        for (var i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++)
        {
            var name = Path.GetFileName(dir);
            if (LooksLikeVersion(name))
                return name;
            dir = Path.GetDirectoryName(dir);
        }

        return VersionFromFileName(Path.GetFileName(sourcePath)) ?? "dev";
    }

    /// <summary>Версия вида «1.2.3» или «1.2.3-a0» (допускается пре-релизный суффикс).</summary>
    private static bool LooksLikeVersion(string s)
    {
        var i = 0;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.'))
            i++;

        if (i == 0 || !s[..i].TrimEnd('.').Contains('.'))
            return false;
        if (i == s.Length)
            return true;

        // Пре-релизный суффикс: -alpha.1 и т.п.
        return s[i] == '-' && s[(i + 1)..].All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');
    }

    /// <summary>Извлекает версию из имени файла, например «mylib.1.2.3.nupkg» → «1.2.3».</summary>
    private static string? VersionFromFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var match = System.Text.RegularExpressions.Regex.Match(
            stem, @"(\d+(?:\.\d+)+)(?:-[0-9A-Za-z.\-]+)?$");
        return match.Success ? match.Value : null;
    }
}
