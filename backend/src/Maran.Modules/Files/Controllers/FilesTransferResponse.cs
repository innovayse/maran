using System.Text;
using Maran.Modules.Files.Models;

namespace Maran.Modules.Files.Controllers;

/// <summary>
/// Writes a file the agent is streaming onto the HTTP response: the headers of a download, in one
/// place, so that every download carries all of them and an error never carries any.
/// </summary>
/// <remarks>
/// <para>
/// <b>A customer's file is never rendered by the browser from the panel's origin.</b> An HTML or SVG
/// file served inline would run in the administrator's session, which is stored cross-site scripting
/// against the person with the most rights. So the type is ALWAYS <c>application/octet-stream</c>,
/// the disposition is ALWAYS <c>attachment</c>, and <c>X-Content-Type-Options: nosniff</c> stops a
/// browser from deciding otherwise from the bytes. There is no preview route for the same reason.
/// </para>
/// <para>
/// The headers are set only after the download has been opened and every refusal has been made, so a
/// problem document never carries a <c>Content-Disposition</c>. It sits in <c>Controllers/</c> because it
/// acts on <see cref="HttpResponse"/> (rules/csharp.md "Common/ versus Services/", test 4).
/// </para>
/// </remarks>
public static class FilesTransferResponse
{
    /// <summary>The only content type a download is sent as.</summary>
    public const string DownloadContentType = "application/octet-stream";

    /// <summary>Stops a browser from deciding a download is something it may render.</summary>
    private const string NoSniff = "nosniff";

    /// <summary>The characters a quoted ASCII file name may not carry: the quote, the backslash, and the percent sign.</summary>
    private const string UnsafeInQuotedName = "\"\\%";

    /// <summary>Sets the headers of a download.</summary>
    /// <param name="response">The response to prepare.</param>
    /// <param name="download">The opened download; its name has already been cleaned of separators and control characters.</param>
    public static void PrepareDownload(HttpResponse response, FileDownload download)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = DownloadContentType;
        response.ContentLength = (long)download.SizeBytes;
        response.Headers.ContentDisposition = DispositionOf(download.FileName);
        response.Headers.XContentTypeOptions = NoSniff;
        response.Headers.CacheControl = "no-store";
    }

    /// <summary>
    /// Builds an <c>attachment</c> disposition with two names: a plain ASCII one for old clients and the
    /// RFC 8187 encoded one that carries the real name.
    /// </summary>
    /// <param name="fileName">The cleaned name.</param>
    /// <returns>The header value.</returns>
    private static string DispositionOf(string fileName)
    {
        var fallback = new StringBuilder(fileName.Length);
        foreach (var character in fileName)
        {
            fallback.Append(character is >= ' ' and < '\u007f' && !UnsafeInQuotedName.Contains(character) ? character : '_');
        }

        return $"attachment; filename=\"{fallback}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }
}
