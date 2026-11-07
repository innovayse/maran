using System.Text.Json;
using Maran.Modules.Identity.Models;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Writes the audit export as one JSON array of objects, a batch at a time, with <see cref="Utf8JsonWriter"/>
/// — the lossless export format, since the CSV one must alter a value that begins like a spreadsheet formula.
/// </summary>
/// <remarks>
/// <para>
/// <b>No string is concatenated into the document.</b> Each row is serialized by
/// <see cref="JsonSerializer"/> into bytes and written onto the writer as one value (the serializer's
/// default encoder escapes quotes, control characters, markup characters and every non-ASCII character,
/// and the writer validates the value), so an attacker-typed subject or user agent cannot break out of
/// its string. The serializer is NOT pointed at the writer: <c>JsonSerializer.Serialize(Utf8JsonWriter, …)</c>
/// ends with a synchronous flush to the stream, and a response body refuses synchronous writes — the
/// failure was a 200 with an empty body, and only a real host shows it, because a memory stream accepts
/// them. Property names are the row's, in camel case — the names the CSV
/// header carries — and a null stays a JSON <c>null</c> rather than disappearing.
/// </para>
/// <para>
/// The writer is flushed after every batch, so the response reaches the client as it is produced and
/// memory holds one batch, not the file. A failure after the first byte leaves an array that is never
/// closed, which no JSON parser accepts: a truncated file cannot be mistaken for a complete one.
/// </para>
/// </remarks>
public static class AuditExportJsonWriter
{
    /// <summary>The serializer's options: camel-case names, nulls written.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Writes the array, flushing to <paramref name="body"/> after each batch.</summary>
    /// <param name="body">The stream the document is written to; left open.</param>
    /// <param name="batches">The rows, in the order they are written, in groups.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>Resolves once the array is closed and flushed.</returns>
    public static async Task WriteAsync(
        Stream body,
        IAsyncEnumerable<IReadOnlyList<AuditExportRow>> batches,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(batches);

        await using var writer = new Utf8JsonWriter(body);

        writer.WriteStartArray();

        await foreach (var batch in batches.WithCancellation(cancellationToken))
        {
            foreach (var row in batch)
            {
                writer.WriteRawValue(JsonSerializer.SerializeToUtf8Bytes(row, SerializerOptions));
            }

            await writer.FlushAsync(cancellationToken);
        }

        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken);
    }
}
