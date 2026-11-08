using System.Text;

namespace Maran.Modules.Files.Domain.Policies;

/// <summary>
/// The file name a download proposes: the entry's own name with everything a header or a file system
/// would misread removed.
/// </summary>
/// <remarks>
/// The name is a customer's text going into a response header. Control characters and path separators
/// are removed, and the length is bounded; what is left is encoded by the response writer (RFC 6266
/// <c>filename*</c>), not here.
/// </remarks>
public static class FilesDownloadNamePolicy
{
    /// <summary>The most characters a proposed name keeps.</summary>
    public const int MaximumCharacters = 150;

    /// <summary>The name proposed when nothing usable is left.</summary>
    public const string FallbackName = "download";

    /// <summary>Cleans a name for a download header.</summary>
    /// <param name="name">The entry's name, or a path whose last segment is used.</param>
    /// <returns>A non-empty, bounded name with no control character, separator or quote.</returns>
    public static string Sanitise(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return FallbackName;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var rune in name.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || rune.Value is '/' or '\\' or '"')
            {
                continue;
            }

            if (builder.Length + rune.Utf16SequenceLength > MaximumCharacters)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        var cleaned = builder.ToString().Trim();
        return cleaned.Length == 0 ? FallbackName : cleaned;
    }
}
