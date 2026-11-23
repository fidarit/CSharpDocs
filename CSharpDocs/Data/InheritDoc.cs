namespace CSharpDocs.Data;

/// <summary>
/// Разрешает тег &lt;inheritdoc&gt;: подставляет описания из указанного члена
/// (&lt;inheritdoc cref="..."/&gt;) или из одноимённого члена базового типа.
/// </summary>
public static class InheritDoc
{
    /// <summary>Дополняет описания типов и членов, заимствуя их по inheritdoc.</summary>
    public static void Resolve(ApiModel model, IReadOnlyDictionary<string, DocBody> docs)
    {
        foreach (var type in model.Types)
            ResolveType(type, docs);

        foreach (var type in model.Types)
            foreach (var member in type.Members)
                ResolveMember(member, docs);
    }

    private static void ResolveType(ApiType type, IReadOnlyDictionary<string, DocBody> docs)
    {
        if (!NeedsInherit(type.Summary, docs, type.DocId))
            return;
        var source = FindBaseDoc(docs, type);
        if (source is null)
            return;
        type.Summary = type.Summary.Length > 0 ? type.Summary : source.Summary;
        type.Remarks = type.Remarks.Length > 0 ? type.Remarks : source.Remarks;
    }

    private static void ResolveMember(ApiMember member, IReadOnlyDictionary<string, DocBody> docs)
    {
        if (!docs.TryGetValue(member.DocId, out var body) || !body.HasInheritDoc)
            return;

        var source = FindSource(body.InheritCref, member, docs);
        if (source is null)
            return;

        if (member.Summary.Length == 0)
            member.Summary = source.Summary;
        if (member.Remarks.Length == 0)
            member.Remarks = source.Remarks;
        if (member.Returns.Length == 0)
            member.Returns = source.Returns;
        if (member.ParamDocs.Count == 0)
            member.ParamDocs = source.Params.ToList();
        if (member.Exceptions.Count == 0)
            member.Exceptions = source.Exceptions.ToList();
    }

    private static DocBody? FindSource(string cref, ApiMember member, IReadOnlyDictionary<string, DocBody> docs)
    {
        // 1) Явная ссылка.
        if (cref.Length > 0 && docs.TryGetValue(cref, out var direct))
            return direct;

        // 2) Одноимённый член базового типа.
        for (var baseType = member.Owner.Parent; baseType is not null; baseType = baseType.Parent)
        {
            var candidate = member.DocId.Replace(member.Owner.FullName, baseType.FullName, StringComparison.Ordinal);
            if (docs.TryGetValue(candidate, out var inherited) && !inherited.IsEmpty)
                return inherited;
        }

        return null;
    }

    private static DocBody? FindBaseDoc(IReadOnlyDictionary<string, DocBody> docs, ApiType type)
    {
        for (var baseType = type.Parent; baseType is not null; baseType = baseType.Parent)
            if (docs.TryGetValue(baseType.DocId, out var body) && !body.IsEmpty)
                return body;
        return null;
    }

    private static bool NeedsInherit(string summary, IReadOnlyDictionary<string, DocBody> docs, string docId) =>
        summary.Length == 0 && docs.TryGetValue(docId, out var body) && body.HasInheritDoc;
}
