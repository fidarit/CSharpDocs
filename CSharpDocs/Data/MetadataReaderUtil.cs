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
    IReadOnlyDictionary<string, MetaMemberSignature> MemberSignatures,
    string Name,
    string Version,
    string TargetFramework);

/// <summary>Идентификатор метода-расширения: тип-контейнер, имя и число параметров.</summary>
public readonly record struct ExtensionMethodKey(string DeclaringType, string MethodName, int ParameterCount);

public sealed record MetaMemberSignature(
    string ReturnType,
    IReadOnlyList<string> MethodTypeParameters,
    IReadOnlyList<string> DeclaringTypeParameters);

/// <summary>
/// Читает структуру типов (наследование, вид, доступность) и признак метода-расширения
/// прямо из управляемых метаданных сборки. Это позволяет обходиться без папки SDK:
/// DLL лежит в том же NuGet-пакете, что и XML-документация.
/// </summary>
public static class MetadataReaderUtil
{
    private const string ExtensionAttribute = "System.Runtime.CompilerServices.ExtensionAttribute";
    private static readonly IReadOnlyDictionary<string, string> PrimitiveTypeNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["System.Boolean"] = "bool", ["System.Byte"] = "byte", ["System.SByte"] = "sbyte",
        ["System.Char"] = "char", ["System.Int16"] = "short", ["System.UInt16"] = "ushort",
        ["System.Int32"] = "int", ["System.UInt32"] = "uint", ["System.Int64"] = "long",
        ["System.UInt64"] = "ulong", ["System.Single"] = "float", ["System.Double"] = "double",
        ["System.Decimal"] = "decimal", ["System.String"] = "string", ["System.Object"] = "object",
        ["System.IntPtr"] = "nint", ["System.UIntPtr"] = "nuint",
    };

    public static MetaAssembly Read(Stream stream)
    {
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        var metadata = pe.GetMetadataReader();
        var enclosing = EnclosingMap(metadata);
        var names = TypeNameMap(metadata, enclosing);
        var namespaces = NamespaceMap(metadata, enclosing);

        var (assemblyName, assemblyVersion, targetFramework) = ReadAssemblyInfo(metadata);

        return new MetaAssembly(
            ReadTypes(metadata, names, namespaces, enclosing),
            ReadExtensionMethods(metadata, names),
            ReadMemberTypes(metadata, names),
            assemblyName,
            assemblyVersion,
            targetFramework);
    }

    private static Dictionary<string, MetaMemberSignature> ReadMemberTypes(
        MetadataReader metadata,
        Dictionary<TypeDefinitionHandle, string> names)
    {
        var result = new Dictionary<string, MetaMemberSignature>(StringComparer.Ordinal);
        var provider = new SignatureTypeProvider(EnclosingMap(metadata));
        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(typeHandle);
            var owner = names[typeHandle];
            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                var signature = method.DecodeSignature(provider, null);
                var rawMethodName = metadata.GetString(method.Name);
                var parameters = string.Join(",", signature.ParameterTypes.Select(t => NormalizeType(t)));
                var methodTypeParameters = method.GetGenericParameters()
                    .Select(handle => metadata.GetString(metadata.GetGenericParameter(handle).Name)).ToArray();
                var declaringTypeParameters = type.GetGenericParameters()
                    .Select(handle => metadata.GetString(metadata.GetGenericParameter(handle).Name)).ToArray();
                var memberName = rawMethodName.Split('`')[0]
                    + (methodTypeParameters.Length > 0 ? "``" + methodTypeParameters.Length : "");
                result[$"{owner}.{memberName}({parameters})"] = new MetaMemberSignature(
                    NormalizeType(signature.ReturnType), methodTypeParameters, declaringTypeParameters);
            }

            foreach (var propertyHandle in type.GetProperties())
            {
                var property = metadata.GetPropertyDefinition(propertyHandle);
                var signature = property.DecodeSignature(provider, null);
                var propertyName = metadata.GetString(property.Name);
                var parameters = string.Join(",", signature.ParameterTypes.Select(t => NormalizeType(t)));
                result[$"{owner}.{propertyName}({parameters})"] = new MetaMemberSignature(
                    NormalizeType(signature.ReturnType), [], type.GetGenericParameters()
                        .Select(handle => metadata.GetString(metadata.GetGenericParameter(handle).Name)).ToArray());
            }
        }
        return result;
    }

    private static string NormalizeType(string type)
    {
        type = type.Replace('/', '.');
        var result = new System.Text.StringBuilder(type.Length);
        for (var i = 0; i < type.Length; i++)
        {
            if (type[i] == '`')
            {
                while (i + 1 < type.Length && char.IsAsciiDigit(type[i + 1]))
                    i++;
            }
            else
            {
                result.Append(type[i]);
            }
        }
        return result.ToString();
    }

    internal static string NormalizeTypeForLookup(string type)
    {
        var result = new System.Text.StringBuilder(type.Length);
        for (var i = 0; i < type.Length;)
        {
            if (type[i] == '_' || char.IsAsciiLetter(type[i]))
            {
                var end = i + 1;
                while (end < type.Length && (type[end] == '_' || type[end] == '.' || char.IsAsciiLetterOrDigit(type[end])))
                    end++;
                var name = type[i..end];
                if (PrimitiveTypeNames.TryGetValue(name, out var primitive))
                    result.Append(primitive);
                else
                    result.Append(name);
                i = end;
            }
            else
            {
                result.Append(type[i]);
                i++;
            }
        }
        return result.ToString();
    }

    private sealed class SignatureTypeProvider(Dictionary<TypeDefinitionHandle, TypeDefinitionHandle> enclosing)
        : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape) =>
            elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";
        public string GetByReferenceType(string elementType) => "ref " + elementType;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";
        public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments) =>
            FormatGenericInstantiation(genericType, typeArguments);
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Void => "void", PrimitiveTypeCode.Boolean => "bool", PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.SByte => "sbyte", PrimitiveTypeCode.Byte => "byte", PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.UInt16 => "ushort", PrimitiveTypeCode.Int32 => "int", PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.Int64 => "long", PrimitiveTypeCode.UInt64 => "ulong", PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.Double => "double", PrimitiveTypeCode.String => "string", PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.IntPtr => "nint", PrimitiveTypeCode.UIntPtr => "nuint", _ => typeCode.ToString(),
        };
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            var name = reader.GetString(type.Name);
            return enclosing.TryGetValue(handle, out var parent)
                ? GetTypeFromDefinition(reader, parent, rawTypeKind) + "+" + name
                : Qualified(reader, reader.GetString(type.Namespace), name);
        }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var reference = reader.GetTypeReference(handle);
            var name = reader.GetString(reference.Name);
            if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
                return GetTypeFromReference(reader, (TypeReferenceHandle)reference.ResolutionScope, rawTypeKind) + "+" + name;

            var ns = reader.GetString(reference.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }

    private static string FormatGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments)
    {
        genericType = genericType.Replace('/', '.');
        var result = new System.Text.StringBuilder(genericType.Length + typeArguments.Sum(t => t.Length) + 2);
        var cumulativeArity = 0;
        for (var i = 0; i < genericType.Length;)
        {
            if (genericType[i] != '`')
            {
                result.Append(genericType[i++]);
                continue;
            }

            i++;
            var arity = 0;
            while (i < genericType.Length && char.IsAsciiDigit(genericType[i]))
                arity = arity * 10 + genericType[i++] - '0';
            if (arity < cumulativeArity || arity > typeArguments.Length)
                continue;

            if (arity > cumulativeArity)
                result.Append('<').Append(string.Join(", ", typeArguments.Skip(cumulativeArity).Take(arity - cumulativeArity))).Append('>');
            cumulativeArity = arity;
        }

        return cumulativeArity == typeArguments.Length
            ? result.ToString()
            : NormalizeType(genericType) + "<" + string.Join(", ", typeArguments) + ">";
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
