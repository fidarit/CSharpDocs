namespace CSharpDocs.Data;

/// <summary>Вид типа.</summary>
public enum TypeKind
{
    Class,
    Struct,
    Enum,
    Delegate,
    Static,
}

/// <summary>Вид члена типа.</summary>
public enum MemberKind
{
    Method,
    Property,
    Field,
    Event,
}

/// <summary>Параметр метода.</summary>
public sealed class ParamInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

/// <summary>Документация параметра из &lt;param&gt;.</summary>
public sealed class ParamDoc
{
    public string Name { get; init; } = "";
    public string Summary { get; init; } = "";
}

/// <summary>Исключение из &lt;exception&gt;.</summary>
public sealed class ExceptionDoc
{
    public string Cref { get; init; } = "";
    public string Summary { get; init; } = "";
}

/// <summary>Метод, свойство, поле или событие.</summary>
public sealed class ApiMember
{
    public MemberKind Kind { get; init; }
    public string Name { get; init; } = "";
    public string FullName { get; init; } = "";
    public string DocId { get; init; } = "";
    public string Slug { get; set; } = "";
    public string Signature { get; set; } = "";
    public List<string> TypeParams { get; set; } = [];
    public string Summary { get; set; } = "";
    public string Remarks { get; set; } = "";
    public string Returns { get; set; } = "";
    public List<ParamInfo> Params { get; set; } = [];
    public List<ParamDoc> ParamDocs { get; set; } = [];
    public List<ExceptionDoc> Exceptions { get; set; } = [];
    public bool IsExtension { get; set; }

    /// <summary>Конструктор типа (не показывается в дереве, только на странице класса).</summary>
    public bool IsConstructor { get; init; }

    public string ExtendType { get; set; } = "";
    public ApiType Owner { get; set; } = null!;

    /// <summary>Короткая подпись члена для таблицы типа.</summary>
    public string Display()
    {
        if (Kind != MemberKind.Method)
            return Name;

        var open = Signature.IndexOf('(');
        var close = Signature.LastIndexOf(')');
        var parameters = Params.Count > 0
            ? string.Join(", ", Params.Select(parameter => parameter.Type))
            : open >= 0 && close > open ? Signature[(open + 1)..close] : "";
        var typeParameters = TypeParams.Count > 0 ? "<" + string.Join(", ", TypeParams) + ">" : "";
        return Name + typeParameters + "(" + parameters + ")";
    }
}

/// <summary>Класс, интерфейс-обёртка, enum или делегат.</summary>
public sealed class ApiType
{
    public string Name { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Namespace { get; init; } = "";
    public string DocId { get; init; } = "";
    public string EnclosingFullName { get; init; } = "";
    public TypeKind Kind { get; init; }
    public string ParentName { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Summary { get; set; } = "";
    public string Remarks { get; set; } = "";
    public List<ApiMember> Members { get; } = [];
    public List<ApiMember> Extensions { get; } = [];
    public List<ApiType> Children { get; } = [];
    public ApiType? Parent { get; set; }

    public string KindLabel => Kind switch
    {
        TypeKind.Class => "class",
        TypeKind.Struct => "struct",
        TypeKind.Enum => "enum",
        TypeKind.Delegate => "delegate",
        TypeKind.Static => "static",
        _ => Kind.ToString().ToLowerInvariant(),
    };
}

/// <summary>Собранная модель API.</summary>
public sealed class ApiModel
{
    public List<ApiType> Types { get; } = [];
    public List<ApiType> Roots { get; } = [];
    public Dictionary<string, ApiType> ByShort { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ApiType> BySlug { get; } = new(StringComparer.Ordinal);
    public string Version { get; set; } = "dev";

    /// <summary>Заголовок документации (задаётся опцией -title).</summary>
    public string Title { get; set; } = "API";

    /// <summary>Метаданные исходной сборки и пакета — для главной страницы.</summary>
    public AssemblyMeta Meta { get; set; } = new();
}

/// <summary>Сведения о сборке и источнике данных.</summary>
public sealed class AssemblyMeta
{
    /// <summary>Простое имя сборки.</summary>
    public string AssemblyName { get; set; } = "";

    /// <summary>Версия сборки (AssemblyVersion).</summary>
    public string AssemblyVersion { get; set; } = "";

    /// <summary>Целевой фреймворк (напр. «.NETCoreApp,Version=v8.0»).</summary>
    public string TargetFramework { get; set; } = "";

    /// <summary>Идентификатор NuGet-пакета (если источник — пакет).</summary>
    public string PackageId { get; set; } = "";

    /// <summary>Версия NuGet-пакета (если источник — пакет).</summary>
    public string PackageVersion { get; set; } = "";

    /// <summary>Описание пакета (если есть).</summary>
    public string PackageDescription { get; set; } = "";

    /// <summary>Авторы (если есть).</summary>
    public string Authors { get; set; } = "";

    /// <summary>Размер XML-документации, байт.</summary>
    public long XmlSize { get; set; }

    /// <summary>Размер сборки, байт.</summary>
    public long DllSize { get; set; }

    /// <summary>Есть ли данные о NuGet-пакете.</summary>
    public bool HasPackage => PackageId.Length > 0;

    /// <summary>Человекочитаемое имя источника: «Пакет Версия» или «Сборка Версия».</summary>
    public string SiteName
    {
        get
        {
            if (HasPackage)
                return PackageVersion.Length > 0 ? $"{PackageId} {PackageVersion}" : PackageId;
            if (AssemblyName.Length > 0)
                return AssemblyVersion.Length > 0 ? $"{AssemblyName} {AssemblyVersion}" : AssemblyName;
            return "";
        }
    }
}
