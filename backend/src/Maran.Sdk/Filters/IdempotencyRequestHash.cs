using System.Globalization;
using System.Text;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Sdk.Filters;

/// <summary>
/// The fingerprint of a request: a keyed HMAC (<see cref="IIdempotencyFingerprint"/>) over its method,
/// the matched route template, the route values and query string in a canonical order, and the body
/// bytes. Two requests with one fingerprint are the same request for idempotency's purposes; one key
/// with two fingerprints is a reused key. It is keyed because the body can carry a password: a plain
/// digest stored beside it could be guessed offline by anyone who can read the table.
/// </summary>
internal static class IdempotencyRequestHash
{
    /// <summary>Separator between the fingerprint's parts; a NUL cannot occur in a method, a template or a decoded value's start.</summary>
    private const char Separator = '\0';

    /// <summary>Computes the fingerprint, buffering the body so the action can still bind it.</summary>
    /// <param name="request">The request, before model binding has touched its body.</param>
    /// <param name="fingerprint">The keyed fingerprint the canonical bytes are passed through.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The 64-character fingerprint, or null when the body is larger than <see cref="IdempotencyLimits.MaxRequestBodyBytes"/>.</returns>
    public static async Task<string?> ComputeAsync(HttpRequest request, IIdempotencyFingerprint fingerprint, CancellationToken ct)
    {
        using var material = new MemoryStream();
        AppendText(material, request.Method.ToUpperInvariant());
        AppendText(material, request.HttpContext.GetEndpoint() is RouteEndpoint endpoint ? endpoint.RoutePattern.RawText ?? string.Empty : string.Empty);

        foreach (var value in request.RouteValues.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            AppendText(material, value.Key + "=" + Convert.ToString(value.Value, CultureInfo.InvariantCulture));
        }

        foreach (var value in request.Query.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            AppendText(material, "?" + value.Key + "=" + string.Join(",", value.Value.OrderBy(text => text, StringComparer.Ordinal)));
        }

        if (request.ContentLength is > IdempotencyLimits.MaxRequestBodyBytes)
        {
            return null;
        }

        // One byte past the cap is enough to know the body is too large, and the buffer limit is set to
        // match so a hostile chunked body can never make the buffering itself unbounded.
        request.EnableBuffering(bufferThreshold: 32 * 1024, bufferLimit: IdempotencyLimits.MaxRequestBodyBytes + 1);
        var buffer = new byte[8192];
        long total = 0;

        try
        {
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer, ct);

                if (read == 0)
                {
                    break;
                }

                total += read;

                if (total > IdempotencyLimits.MaxRequestBodyBytes)
                {
                    request.Body.Position = 0;
                    return null;
                }

                material.Write(buffer, 0, read);
            }
        }
        catch (IOException)
        {
            // The buffer limit is one byte past the cap, but a read can carry the total several KiB past
            // it at once and the buffering stream then throws instead of returning: that is the same
            // "body too large" answer, not a server fault.
            return null;
        }

        request.Body.Position = 0;
        return fingerprint.Of(material.ToArray());
    }

    /// <summary>Writes one text part, followed by the separator, into the material being fingerprinted.</summary>
    /// <param name="material">The canonical bytes collected so far.</param>
    /// <param name="text">The part.</param>
    private static void AppendText(MemoryStream material, string text)
    {
        material.Write(Encoding.UTF8.GetBytes(text + Separator));
    }
}
