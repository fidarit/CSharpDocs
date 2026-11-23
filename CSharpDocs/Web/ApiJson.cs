using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using CSharpDocs.Data;

namespace CSharpDocs.Web;

public static class ApiJson
{
    private static readonly ApiJsonContext Context = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    });

    public static string BuildTree(ApiModel model) =>
        JsonSerializer.Serialize(ApiTree.Build(model).Select(ToJsonNode).ToList(), Context.ListApiTreeNodeJson);

    public static string BuildCounts(IReadOnlyDictionary<string, int> counts) =>
        JsonSerializer.Serialize(counts, Context.IReadOnlyDictionaryStringInt32);

    private static ApiTreeNodeJson ToJsonNode(TreeNode node) => new()
    {
        Name = node.Name,
        Kind = node.Kind,
        Key = node.Key,
        Full = node.Full.Length > 0 && node.Full != node.Name ? node.Full : null,
        Search = node.Search.Length > 0 ? node.Search : null,
        Slug = node.Slug.Length > 0 ? node.Slug : null,
        Ext = node.Ext > 0 ? node.Ext : null,
        Children = node.Children.Count > 0 ? node.Children.Select(ToJsonNode).ToList() : null,
    };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ApiTreeNodeJson>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, int>))]
internal partial class ApiJsonContext : JsonSerializerContext;

internal sealed class ApiTreeNodeJson
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Key { get; init; } = "";
    public string? Full { get; init; }
    public string? Search { get; init; }
    public string? Slug { get; init; }
    public int? Ext { get; init; }
    public List<ApiTreeNodeJson>? Children { get; init; }
}
