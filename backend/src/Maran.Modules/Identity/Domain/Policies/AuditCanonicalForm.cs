using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Maran.Modules.Identity.Domain.Entities;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// The byte sequences the audit chain hashes: one for the columns an erasure never touches and one for
/// the columns it may replace. Both are built the same way, so the same row always yields the same bytes
/// and two different rows can never yield the same ones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every field is length-prefixed</b> (four bytes, big-endian, then the UTF-8 text; an absent value is
/// the length <c>0xFFFFFFFF</c> with no text). Without the prefix, the pairs ("ab", "c") and ("a", "bc")
/// would concatenate to the same bytes and a field could be moved across a boundary without changing the
/// hash. An absent value and an empty string are different on purpose.
/// </para>
/// <para>
/// <b>The instant is truncated to the microsecond</b>, the precision PostgreSQL stores. A row that is
/// hashed in memory with a tick-level instant and read back from the table with a microsecond one would
/// otherwise hash differently the second time and read as tampered.
/// </para>
/// </remarks>
public static class AuditCanonicalForm
{
    /// <summary>The length written in place of a field that has no value.</summary>
    private const uint AbsentLength = 0xFFFFFFFF;

    /// <summary>The columns an erasure never changes: identity, instant, action, outcome and correlation id.</summary>
    /// <param name="row">The row to describe.</param>
    /// <returns>The canonical bytes of the row's immutable columns.</returns>
    public static byte[] Immutable(AuditEvent row)
    {
        ArgumentNullException.ThrowIfNull(row);

        using var stream = new MemoryStream();

        WriteField(stream, row.Id.ToString("D", CultureInfo.InvariantCulture));
        WriteField(stream, TruncatedToMicrosecond(row.OccurredAt).ToString(CultureInfo.InvariantCulture));
        WriteField(stream, row.Action);
        WriteField(stream, row.Succeeded ? "1" : "0");
        WriteField(stream, row.CorrelationId);

        return stream.ToArray();
    }

    /// <summary>
    /// The columns an erasure may replace: who acted and who was viewing, what it was about (the subject text and the
    /// login it names, when it names one), where it came from and the account it concerns.
    /// </summary>
    /// <param name="row">The row to describe.</param>
    /// <returns>The canonical bytes of the row's erasable columns.</returns>
    public static byte[] Erasable(AuditEvent row)
    {
        ArgumentNullException.ThrowIfNull(row);

        using var stream = new MemoryStream();

        WriteField(stream, row.ActorUserId?.ToString("D", CultureInfo.InvariantCulture));
        WriteField(stream, row.ActorUsername);
        WriteField(stream, row.Subject);
        WriteField(stream, row.IpAddress);
        WriteField(stream, row.UserAgent);
        WriteField(stream, row.AccountId?.ToString("D", CultureInfo.InvariantCulture));
        WriteField(stream, row.Impersonator?.OperatorId.ToString("D", CultureInfo.InvariantCulture));
        WriteField(stream, row.Impersonator?.Username);

        // Appended only when present, so a row written before the column existed (and any row without a
        // subject login) hashes to exactly the bytes it was sealed with. A present value adds forty
        // bytes, so the two forms can never be the same sequence.
        if (row.SubjectUserId is { } subjectUserId)
        {
            WriteField(stream, subjectUserId.ToString("D", CultureInfo.InvariantCulture));
        }

        return stream.ToArray();
    }

    /// <summary>The instant as UTC ticks, with everything below one microsecond dropped.</summary>
    /// <param name="instant">The instant to truncate.</param>
    /// <returns>The tick count, a multiple of <see cref="TimeSpan.TicksPerMicrosecond"/>.</returns>
    private static long TruncatedToMicrosecond(DateTimeOffset instant)
    {
        var ticks = instant.UtcTicks;

        return ticks - (ticks % TimeSpan.TicksPerMicrosecond);
    }

    /// <summary>Appends one length-prefixed field.</summary>
    /// <param name="stream">Where the bytes go.</param>
    /// <param name="value">The field's text, or <c>null</c> for an absent value.</param>
    private static void WriteField(MemoryStream stream, string? value)
    {
        Span<byte> prefix = stackalloc byte[sizeof(uint)];

        if (value is null)
        {
            BinaryPrimitives.WriteUInt32BigEndian(prefix, AbsentLength);
            stream.Write(prefix);

            return;
        }

        var text = Encoding.UTF8.GetBytes(value);

        BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)text.Length);
        stream.Write(prefix);
        stream.Write(text);
    }
}
