using CSharpDocs.Data;
using CSharpDocs.Web;

namespace CSharpDocs.Tests;

public class FixTests
{
    [Fact]
    public void InternalTypes_Are_Excluded()
    {
        var model = TestModel.LoadOrSkip();
        Assert.DoesNotContain(model.Types, t => t.FullName.Contains(".Internal.", StringComparison.Ordinal));
    }

    [Fact]
    public void InheritDoc_Resolves_Extension_Methods()
    {
        var model = TestModel.LoadOrSkip();
        var extensions = model.Types.SelectMany(t => t.Extensions).ToList();
        if (extensions.Count == 0)
            Assert.Skip("в сэмпле нет методов-расширений");

        // Хотя бы часть расширений должна получить описание через <inheritdoc>.
        Assert.Contains(extensions, m => m.Summary.Length > 0);
    }

    [Fact]
    public void Tree_Json_Has_Namespaces_Types_And_Members()
    {
        var model = TestModel.LoadOrSkip();
        var json = ApiJson.BuildTree(model);

        // пространства имён (kind=namespace), типы и члены присутствуют
        Assert.Contains("\"kind\":\"namespace\"", json);
        Assert.Contains("\"kind\":\"класс\"", json);
        Assert.Contains("\"kind\":\"метод\"", json);
    }
}
