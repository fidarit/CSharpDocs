using CSharpDocs.Data;

namespace CSharpDocs.Tests;

/// <summary>
/// Загружает модель из сэмпла, заданного переменной окружения CSHARPDOCS_XML
/// (путь к XML-документации; DLL ищется рядом как &lt;Assembly&gt;.dll).
/// Тесты не привязаны к конкретной библиотеке.
/// </summary>
internal static class TestModel
{
    private static ApiModel? _cached;

    public static bool TryLoad(out ApiModel model)
    {
        if (_cached is not null)
        {
            model = _cached;
            return true;
        }

        var xml = Environment.GetEnvironmentVariable("CSHARPDOCS_XML");
        if (string.IsNullOrEmpty(xml) || !File.Exists(xml))
        {
            model = null!;
            return false;
        }

        using var xmlStream = File.OpenRead(xml);
        using var dllStream = File.OpenRead(Path.ChangeExtension(xml, ".dll"));
        _cached = ModelBuilder.Build(xmlStream, dllStream, "test");
        model = _cached;
        return true;
    }

    public static ApiModel LoadOrSkip()
    {
        if (TryLoad(out var model))
            return model;
        Assert.Skip("Сэмпл не задан: установите CSHARPDOCS_XML");
        return null!; // недостижимо
    }
}
