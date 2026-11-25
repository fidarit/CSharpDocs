using CSharpDocs.Data;

namespace CSharpDocs.Tests;

public class ModelTests
{
    [Fact]
    public void Method_Signature_Handles_Namespaced_Parameters_And_Partial_Param_Docs()
    {
        var xml = $"""
            <doc><members>
              <member name="T:CSharpDocs.Tests.SignatureFixture"><summary /></member>
              <member name="T:CSharpDocs.Tests.SignatureWidget"><summary /></member>
              <member name="M:CSharpDocs.Tests.SignatureFixture.Transform(CSharpDocs.Tests.SignatureWidget,System.Int32)">
                <summary /><param name="count">Count.</param>
              </member>
              <member name="M:CSharpDocs.Tests.SignatureFixture.Echo``1(``0)">
                <summary /><param name="value">Value.</param>
              </member>
              <member name="M:CSharpDocs.Tests.SignatureFixture.GetItems"><summary /></member>
              <member name="M:CSharpDocs.Tests.SignatureFixture.GetKeys"><summary /></member>
              <member name="M:CSharpDocs.Tests.SignatureFixture.GetMatrix"><summary /></member>
              <member name="M:CSharpDocs.Tests.SignatureFixture.SumMatrix(System.Int32[0:,0:])">
                <summary /><param name="matrix">Matrix.</param>
              </member>
              <member name="P:CSharpDocs.Tests.SignatureFixture.Item(System.Int32)">
                <summary /><param name="index">Index.</param>
              </member>
            </members></doc>
            """;
        using var xmlStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        using var dllStream = File.OpenRead(typeof(SignatureFixture).Assembly.Location);
        var model = ModelBuilder.Build(xmlStream, dllStream, "test");
        var member = Assert.Single(model.ByShort[nameof(SignatureFixture)].Members, m => m.Name == "Transform");

        Assert.Contains("SignatureWidget SignatureFixture.Transform(", member.Signature);
        Assert.DoesNotContain("count", member.Signature);

        var generic = Assert.Single(model.ByShort[nameof(SignatureFixture)].Members, m => m.Name == "Echo");
        Assert.Equal("TResult", generic.ReturnType);
        Assert.Equal("TResult SignatureFixture.Echo<TResult>(TResult value)", generic.Signature);

        var items = Assert.Single(model.ByShort[nameof(SignatureFixture)].Members, m => m.Name == "GetItems");
        Assert.Equal("List<int>", items.ReturnType);
        Assert.StartsWith("List<int>", items.Signature);

        var keys = Assert.Single(model.ByShort[nameof(SignatureFixture)].Members, m => m.Name == "GetKeys");
        Assert.Equal("Dictionary<int, string>.KeyCollection", keys.ReturnType);

        var matrix = Assert.Single(model.ByShort[nameof(SignatureFixture)].Members, m => m.Name == "GetMatrix");
        Assert.Equal("int[,]", matrix.ReturnType);
        Assert.Contains("int[,]", matrix.Signature);

        var matrixSum = Assert.Single(model.ByShort[nameof(SignatureFixture)].Members, m => m.Name == "SumMatrix");
        Assert.Contains("int[,] matrix", matrixSum.Signature);

        var indexer = Assert.Single(model.ByShort[nameof(SignatureFixture)].Members, m => m.Name == "Item");
        Assert.Contains("int index", indexer.Signature);
    }

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

public sealed class SignatureFixture
{
    public SignatureWidget Transform(SignatureWidget input, int count) => input;
    public TResult Echo<TResult>(TResult value) => value;
    public List<int> GetItems() => [];
    public Dictionary<int, string>.KeyCollection GetKeys() => null!;
    public int[,] GetMatrix() => new int[0, 0];
    public int SumMatrix(int[,] matrix) => matrix.Length;
    public int this[int index] => index;
}

public sealed class SignatureWidget { }
