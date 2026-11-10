namespace Maran.Host.Modules;

/// <summary>
/// The one rule that says which file of a packaged module's screen bundle the panel will serve and as
/// what. A path is served only when every segment is a plain name, the file's extension is on a short
/// allow-list, and the content type is the one this rule assigns to that extension - never one the
/// package or its loader names. A document extension is not on the list, so a package can never make
/// the panel serve a page of its own: the only document of a frame is the one the Host generates
/// (<see cref="ModuleFrameDocument"/>).
/// </summary>
public static class ModuleFramePathRule
{
    /// <summary>The most segments a served path may have; a bundle is a few folders deep, not a tree.</summary>
    public const int MaxSegments = 4;

    /// <summary>The most characters one segment may have.</summary>
    public const int MaxSegmentLength = 100;

    /// <summary>The extension, with its dot, and the content type the panel serves it as.</summary>
    private static readonly Dictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".js"] = "text/javascript",
            [".css"] = "text/css",
            [".json"] = "application/json",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp",
            [".woff2"] = "font/woff2",
            [".svg"] = "image/svg+xml",
        };

    /// <summary>Reads a path from the request into the relative path the window is asked for.</summary>
    /// <param name="path">The route's catch-all value; null or empty for the frame's folder URL.</param>
    /// <param name="relativePath">The path with its segments joined by <c>/</c>, when it is servable.</param>
    /// <returns>True when every segment is a plain name, within the limits, and the extension is allowed.</returns>
    public static bool TryParse(string? path, out string relativePath)
    {
        relativePath = string.Empty;

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var segments = path.Split('/');
        if (segments.Length > MaxSegments)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (!IsPlainSegment(segment))
            {
                return false;
            }
        }

        if (ContentTypeOf(segments[^1]) is null)
        {
            return false;
        }

        relativePath = string.Join('/', segments);
        return true;
    }

    /// <summary>Whether a value is a plain file name the panel would serve: one segment, an allowed extension.</summary>
    /// <param name="fileName">The value, for example a description's entry.</param>
    /// <returns>True when it is a single plain segment with an allowed extension.</returns>
    public static bool IsServableFileName(string? fileName)
    {
        return fileName is not null && IsPlainSegment(fileName) && ContentTypeOf(fileName) is not null;
    }

    /// <summary>The content type the panel serves a file as.</summary>
    /// <param name="fileName">The file's name or path; only its extension is read.</param>
    /// <returns>The fixed content type of the extension, or null when the extension is not allowed.</returns>
    public static string? ContentTypeOf(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        if (dot < 0)
        {
            return null;
        }

        return ContentTypes.TryGetValue(fileName[dot..], out var contentType) ? contentType : null;
    }

    /// <summary>Whether one segment is a plain name: allowed characters only, not <c>.</c> or <c>..</c>, within the length limit.</summary>
    /// <param name="segment">The segment.</param>
    /// <returns>True when the segment may be part of a served path.</returns>
    private static bool IsPlainSegment(string segment)
    {
        if (segment.Length is 0 or > MaxSegmentLength || segment is "." or "..")
        {
            return false;
        }

        foreach (var character in segment)
        {
            var allowed = character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '.' or '_' or '-';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
