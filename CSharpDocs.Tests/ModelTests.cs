using CSharpDocs.Data;

namespace CSharpDocs.Tests;

public class ModelTests
{
    [Fact]
    public void Loads_Types_And_Members()
    {
        var model = TestModel.LoadOrSkip();
        Assert.NotEmpty(model.Types);
        Assert.All(model.Types, t => Assert.False(string.IsNullOrWhiteSpace(t.Name)));
    }

    [Fact]
    public void Hierarchy_Is_Linked()
    {
        var model = TestModel.LoadOrSkip();
        foreach (var type in model.Types)
        {
            if (type.ParentName.Length == 0 || !model.ByShort.TryGetValue(type.ParentName, out var parent))
                continue;
            Assert.Same(parent, type.Parent);
            Assert.Contains(type, parent.Children);
        }
    }

    [Fact]
    public void Extensions_Are_Attached_To_Receiver()
    {
        var model = TestModel.LoadOrSkip();
        var extensions = model.Types.SelectMany(t => t.Extensions).ToList();
        Assert.All(extensions, m =>
        {
            Assert.True(m.IsExtension);
            Assert.False(string.IsNullOrEmpty(m.ExtendType));
        });
    }

    [Fact]
    public void Public_Types_Only()
    {
        var model = TestModel.LoadOrSkip();
        // internal-типы отфильтрованы: их namespace не должен содержать .Internal
        Assert.DoesNotContain(model.Types, t => t.FullName.Contains(".Internal.", StringComparison.Ordinal));
    }
}
