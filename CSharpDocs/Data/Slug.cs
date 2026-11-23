using System.Globalization;
using System.Text;

namespace CSharpDocs.Data;

/// <summary>Формирование URL-слагов.</summary>
public static class Slug
{
    public static string ForType(string fullName) => Sanitize(fullName);

    public static string ForMember(MemberKind kind, string fullName) =>
        Sanitize(fullName) + "-" + Hash8(kind.ToString() + ":" + fullName);

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        var prevDash = false;
        foreach (var ch in value)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevDash = false;
            }
            else if (!prevDash && sb.Length > 0)
            {
                sb.Append('-');
                prevDash = true;
            }
        }
        return sb.ToString().Trim('-');
    }

    private static string Hash8(string value)
    {
        // FNV-1a, 32 бита.
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }
        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}
