using System.IO.Compression;
using System.Xml.Linq;

namespace CSharpDocs.Data;

/// <summary>Метаданные NuGet-пакета.</summary>
public sealed record PackageMeta(string Id, string Version, string Description, string Authors);

/// <summary>Извлечение XML-документации и сборки из NuGet-пакета.</summary>
public static class PackageSource
{
    /// <summary>
    /// Достаёт (xml, dll) из lib/&lt;tfm&gt;/ пакета, предпочитая net8.0.
    /// </summary>
    public static (byte[] Xml, byte[] Dll, PackageMeta Meta) FromNupkg(string path)
    {
        using var zip = ZipFile.OpenRead(path);

        var meta = ReadNuspec(zip) ?? FallbackMeta(path);

        ZipArchiveEntry? xmlEntry = null;
        ZipArchiveEntry? dllEntry = null;
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith("lib/", StringComparison.Ordinal))
                continue;
            if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                (xmlEntry is null || Rank(entry.FullName) < Rank(xmlEntry.FullName)))
                xmlEntry = entry;
            if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                (dllEntry is null || Rank(entry.FullName) < Rank(dllEntry.FullName)))
                dllEntry = entry;
        }

        if (xmlEntry is null)
            throw new InvalidOperationException($"В пакете {path} нет lib/*.xml");
        if (dllEntry is null)
            throw new InvalidOperationException($"В пакете {path} нет lib/*.dll");

        return (ReadEntry(xmlEntry), ReadEntry(dllEntry), meta);
    }

    /// <summary>Читает метаданные из .nuspec внутри пакета.</summary>
    private static PackageMeta? ReadNuspec(ZipArchive zip)
    {
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return null;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        // nuspec содержит xmlns — сравниваем по локальному имени, игнорируя namespace.
        var metadata = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata");
        if (metadata is null)
            return null;

        string Value(string name) => metadata.Elements()
            .FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";
        return new PackageMeta(Value("id"), Value("version"), Value("description"), Value("authors"));
    }

    /// <summary>Запасной вариант: имя и версия из пути к пакету.</summary>
    private static PackageMeta FallbackMeta(string path)
    {
        var version = Path.GetFileName(Path.GetDirectoryName(path) ?? "") ?? "";
        var id = Path.GetFileName(path).Split('.')[0];
        return new PackageMeta(id, version, "", "");
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static int Rank(string name) => name switch
    {
        _ when name.Contains("/net8.0/", StringComparison.Ordinal) => 0,
        _ when name.Contains("/net7.0/", StringComparison.Ordinal) => 1,
        _ when name.Contains("/net6.0/", StringComparison.Ordinal) => 2,
        _ when name.Contains("/netstandard2.0/", StringComparison.Ordinal) => 3,
        _ => 10,
    };

    /// <summary>
    /// Ищет последний по версии .nupkg указанного пакета в NuGet-кэше.
    /// Имя пакета задаётся параметром (lowercase), проект от него не зависит.
    /// </summary>
    public static string? FindLatestNupkg(string nugetCacheDir, string packageId)
    {
        var pkgDir = Path.Combine(nugetCacheDir, packageId.ToLowerInvariant());
        if (!Directory.Exists(pkgDir))
            return null;

        return Directory.EnumerateFiles(pkgDir, "*.nupkg", SearchOption.AllDirectories)
            .OrderByDescending(path => VersionKey(path), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string VersionKey(string path)
    {
        var version = Path.GetFileName(Path.GetDirectoryName(path) ?? "") ?? "";
        // Преобразуем 25.5.9 -> 0025.0005.0009 для строкового сравнения.
        var parts = version.Split('.');
        var key = "";
        foreach (var p in parts)
            key += int.TryParse(p, out var n) ? n.ToString("D4") : p;
        return key;
    }
}
