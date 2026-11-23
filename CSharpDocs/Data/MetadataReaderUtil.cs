using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace CSharpDocs.Data;

/// <summary>Сведения о типе, прочитанные из метаданных сборки.</summary>
/// <param name="FullName">Полное имя, включая вложенность через точку.</param>
/// <param name="BaseName">Короткое имя базового типа (например, IModelObject).</param>
/// <param name="Namespace">Пространство имён (для вложенных — как у внешнего типа).</param>
/// <param name="Kind">Вид типа.</param>
/// <param name="IsPublic">Тип виден за пределами сборки.</param>
/// <param name="EnclosingFullName">Полное имя внешнего типа для вложенных, иначе пусто.</param>
public sealed record MetaType(
    string FullName,
    string BaseName,
    string Namespace,
    TypeKind Kind,
    bool IsPublic,
    string EnclosingFullName);

/// <summary>Сведения о сборке: типы, методы-расширения и метаданные самой сборки.</summary>
/// <param name="Types">Все определённые типы.</param>
/// <param name="ExtensionMethods">Ключи методов с модификатором this у первого параметра.</param>
/// <param name="Name">Простое имя сборки.</param>
/// <param name="Version">Версия сборки (AssemblyVersion).</param>
/// <param name="TargetFramework">Целевой фреймворк (TargetFrameworkAttribute), если указан.</param>
public sealed record MetaAssembly(
    IReadOnlyList<MetaType> Types,
    IReadOnlySet<ExtensionMethodKey> ExtensionMethods,
    string Name,
    string Version,
    string TargetFramework);

/// <summary>Идентификатор метода-расширения: тип-контейнер, имя и число параметров.</summary>
public readonly record struct ExtensionMethodKey(string DeclaringType, string MethodName, int ParameterCount);

/// <summary>
/// Читает структуру типов (наследование, вид, доступность) и признак метода-расширения
/// прямо из управляемых метаданных сборки. Это позволяет обходиться без папки SDK:
/// DLL лежит в том же NuGet-пакете, что и XML-документация.
/// </summary>
public static class MetadataReaderUtil
{
    private const string ExtensionAttribute = "System.Runtime.CompilerServices.ExtensionAttribute";

    public static MetaAssembly Read(Stream stream)
    {
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var enclosing = EnclosingMap(metadata);
        var names = TypeNameMap(metadata, enclosing);
        var namespaces = NamespaceMap(metadata, enclosing);

        var (assemblyName, assemblyVersion, targetFramework) = ReadAssemblyInfo(metadata);

        return new MetaAssembly(
            ReadTypes(metadata, names, namespaces, enclosing),
            ReadExtensionMethods(metadata, names),
            assemblyName,
            assemblyVersion,
            targetFramework);
    }

    /// <summary>Читает имя, версию сборки и целевой фреймворк.</summary>
    private static (string Name, string Version, string TargetFramework) ReadAssemblyInfo(MetadataReader metadata)
    {
        var definition = metadata.GetAssemblyDefinition();
        var name = metadata.GetString(definition.Name);
        var version = definition.Version.ToString();

        var targetFramework = "";
        foreach (var attributeHandle in definition.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(attributeHandle);
            if (AttributeTypeName(metadata, attribute) != "System.Runtime.Versioning.TargetFrameworkAttribute")
                continue;

            var decoded = attribute.DecodeValue(TargetFrameworkValueProvider.Instance);
            if (decoded.FixedArguments.Length > 0 && decoded.FixedArguments[0].Value is string value)
                targetFramework = value;
            break;
        }
        return (name, version, targetFramework);
    }

    /// <summary>Декодер значений атрибутов (нужен только первый строковый аргумент).</summary>
    private sealed class TargetFrameworkValueProvider : ICustomAttributeTypeProvider<string>
    {
        public static readonly TargetFrameworkValueProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSystemType() => "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeDefinition(handle).Name);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeReference(handle).Name);
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(string type) => type == "System.Type";
    }

    private static List<MetaType> ReadTypes(
        MetadataReader metadata,
        Dictionary<TypeDefinitionHandle, string> names,
        Dictionary<TypeDefinitionHandle, string> namespaces,
        Dictionary<TypeDefinitionHandle, TypeDefinitionHandle> enclosing)
    {
        var result = new List<MetaType>();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var typeDef = metadata.GetTypeDefinition(handle);
            if (metadata.GetString(typeDef.Name) == "<Module>")
                continue;

            var baseFullName = BaseFullName(metadata, typeDef, names);
            var isNested = enclosing.TryGetValue(handle, out var parent);

            result.Add(new MetaType(
                FullName: names[handle],
                BaseName: ShortName(baseFullName),
                Namespace: namespaces[handle],
                Kind: KindOf(baseFullName, typeDef.Attributes),
                IsPublic: IsPublic(typeDef.Attributes),
                EnclosingFullName: isNested ? names[parent] : ""));
        }
        return result;
    }

    /// <summary>
    /// Собирает методы-расширения. В IL модификатор <c>this</c> у первого параметра
    /// выражается атрибутом [Extension] на методе; приёмник — это первый параметр.
    /// </summary>
    private static HashSet<ExtensionMethodKey> ReadExtensionMethods(
        MetadataReader metadata,
        Dictionary<TypeDefinitionHandle, string> names)
    {
        var result = new HashSet<ExtensionMethodKey>();
        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var typeDef = metadata.GetTypeDefinition(typeHandle);
            if (!IsStatic(typeDef.Attributes))
                continue;

            var declaringType = names[typeHandle];
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                if (!HasExtensionAttribute(metadata, method))
                    continue;

                result.Add(new ExtensionMethodKey(
                    declaringType,
                    metadata.GetString(method.Name),
                    method.GetParameters().Count));
            }
        }
        return result;
    }

    private static bool HasExtensionAttribute(MetadataReader metadata, MethodDefinition method)
    {
        foreach (var attributeHandle in method.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(attributeHandle);
            if (AttributeTypeName(metadata, attribute) == ExtensionAttribute)
                return true;
        }
        return false;
    }

    private static string AttributeTypeName(MetadataReader metadata, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                return member.Parent.Kind == HandleKind.TypeReference
                    ? Qualified(metadata, metadata.GetTypeReference((TypeReferenceHandle)member.Parent))
                    : "";
            case HandleKind.MethodDefinition:
                var def = metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                var type = metadata.GetTypeDefinition(def.GetDeclaringType());
                return Qualified(metadata, metadata.GetString(type.Namespace), metadata.GetString(type.Name));
            default:
                return "";
        }
    }

    private static Dictionary<TypeDefinitionHandle, string> TypeNameMap(
        MetadataReader metadata,
        Dictionary<TypeDefinitionHandle, TypeDefinitionHandle> enclosing)
    {
        var names = new Dictionary<TypeDefinitionHandle, string>();

        string FullName(TypeDefinitionHandle handle)
        {
            if (names.TryGetValue(handle, out var cached))
                return cached;

            var typeDef = metadata.GetTypeDefinition(handle);
            var name = metadata.GetString(typeDef.Name);
            var ns = metadata.GetString(typeDef.Namespace);
            var full = enclosing.TryGetValue(handle, out var parent)
                ? FullName(parent) + "." + name
                : ns.Length > 0 ? ns + "." + name : name;

            names[handle] = full;
            return full;
        }

        foreach (var handle in metadata.TypeDefinitions)
            FullName(handle);
        return names;
    }

    /// <summary>
    /// Пространство имён типа. У вложенного типа оно совпадает с внешним —
    /// в метаданных у вложенных типов Namespace пуст.
    /// </summary>
    private static Dictionary<TypeDefinitionHandle, string> NamespaceMap(
        MetadataReader metadata,
        Dictionary<TypeDefinitionHandle, TypeDefinitionHandle> enclosing)
    {
        var map = new Dictionary<TypeDefinitionHandle, string>();

        string NamespaceOf(TypeDefinitionHandle handle)
        {
            if (map.TryGetValue(handle, out var cached))
                return cached;

            var value = enclosing.TryGetValue(handle, out var parent)
                ? NamespaceOf(parent)
                : metadata.GetString(metadata.GetTypeDefinition(handle).Namespace);

            map[handle] = value;
            return value;
        }

        foreach (var handle in metadata.TypeDefinitions)
            NamespaceOf(handle);
        return map;
    }

    private static Dictionary<TypeDefinitionHandle, TypeDefinitionHandle> EnclosingMap(MetadataReader metadata)
    {
        var map = new Dictionary<TypeDefinitionHandle, TypeDefinitionHandle>();
        foreach (var handle in metadata.TypeDefinitions)
            foreach (var nested in metadata.GetTypeDefinition(handle).GetNestedTypes())
                map[nested] = handle;
        return map;
    }

    private static string BaseFullName(
        MetadataReader metadata,
        TypeDefinition typeDef,
        Dictionary<TypeDefinitionHandle, string> names)
    {
        if (typeDef.BaseType.IsNil)
            return "";
        return typeDef.BaseType.Kind switch
        {
            HandleKind.TypeReference => Qualified(metadata, metadata.GetTypeReference((TypeReferenceHandle)typeDef.BaseType)),
            HandleKind.TypeDefinition => names[(TypeDefinitionHandle)typeDef.BaseType],
            _ => "",
        };
    }

    private static string Qualified(MetadataReader metadata, TypeReference typeRef) =>
        Qualified(metadata, metadata.GetString(typeRef.Namespace), metadata.GetString(typeRef.Name));

    private static string Qualified(MetadataReader metadata, string ns, string name) =>
        ns.Length == 0 ? name : ns + "." + name;

    private static string ShortName(string fullName)
    {
        var dot = fullName.LastIndexOf('.');
        return dot >= 0 ? fullName[(dot + 1)..] : fullName;
    }

    private static TypeKind KindOf(string baseFullName, TypeAttributes attributes) => baseFullName switch
    {
        "System.Enum" => TypeKind.Enum,
        "System.MulticastDelegate" => TypeKind.Delegate,
        "System.ValueType" => TypeKind.Struct,
        _ => IsStatic(attributes) ? TypeKind.Static : TypeKind.Class,
    };

    private static bool IsStatic(TypeAttributes attributes) =>
        (attributes & TypeAttributes.Abstract) != 0 && (attributes & TypeAttributes.Sealed) != 0;

    private static bool IsPublic(TypeAttributes attributes) =>
        (attributes & TypeAttributes.VisibilityMask) is TypeAttributes.Public or TypeAttributes.NestedPublic;
}
