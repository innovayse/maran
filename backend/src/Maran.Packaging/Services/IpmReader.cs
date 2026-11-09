using System.IO.Compression;
using System.Text;
using Maran.Packaging.Constants;
using Maran.Packaging.Models;
using Maran.Packaging.Options;
using Maran.Packaging.Resources;

namespace Maran.Packaging.Services;

/// <summary>
/// Reads an <c>.ipm</c> archive safely, before anything in it is believed. The input is hostile bytes
/// until a signature says otherwise, and the signature can only be checked on what this reader hands
/// over, so the reader defends itself: it caps the entry count, each entry and the total, judges the
/// compression ratio, refuses every name that is not contained (<see cref="IpmEntryNameRule"/>),
/// refuses entries that are not plain files (a symbolic link, a directory), duplicates and
/// case-insensitive collisions, and never trusts a declared size without counting the bytes.
/// </summary>
/// <remarks>
/// It writes nothing to disk and loads nothing: the result is in memory, and extraction (with its own
/// descriptor-anchored containment) is the loader's step, after verification. Memory is bounded by
/// <see cref="IpmLimits.MaxTotalBytes"/> whatever the archive claims.
/// </remarks>
public sealed class IpmReader
{
    /// <summary>The buffer size of a bounded read.</summary>
    private const int ReadBufferBytes = 81920;

    /// <summary>The mask of the file-type bits in a Unix mode held in a ZIP entry's external attributes.</summary>
    private const int UnixFileTypeMask = 0xF000;

    /// <summary>The Unix file type of a regular file.</summary>
    private const int UnixRegularFile = 0x8000;

    /// <summary>Entry names are read as strict UTF-8, so a byte sequence that is not valid text is refused, not guessed at.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>The caps this reader enforces.</summary>
    private readonly IpmLimits _limits;

    /// <summary>Creates a reader.</summary>
    /// <param name="limits">The caps to hold an archive to; <see cref="IpmLimits.Default"/> for a panel.</param>
    public IpmReader(IpmLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _limits = limits;
    }

    /// <summary>Reads an archive.</summary>
    /// <param name="package">A readable stream holding the archive; not disposed.</param>
    /// <returns>The archive, or the first refusal: empty, malformed, uncontained, too large, duplicated, not a file, or missing its manifest or signature.</returns>
    public Result<IpmArchive> Read(Stream package)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (!package.CanRead)
        {
            throw new ArgumentException("The package stream must be readable.", nameof(package));
        }

        var buffered = Buffer(package);

        if (!buffered.IsSuccess)
        {
            return Result<IpmArchive>.Fail(buffered.Error!);
        }

        try
        {
            using var zip = new ZipArchive(buffered.Value, ZipArchiveMode.Read, leaveOpen: true, StrictUtf8);
            return ReadEntries(zip);
        }
        catch (DecoderFallbackException)
        {
            return Fail(nameof(ErrorMessages.IpmEntryNameInvalid));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or NotSupportedException)
        {
            return Fail(nameof(ErrorMessages.IpmArchiveMalformed));
        }
    }

    /// <summary>Holds the package in a seekable buffer no larger than the total cap, which no honest archive exceeds.</summary>
    /// <param name="package">The stream to buffer.</param>
    /// <returns>A seekable stream at position zero, or the refusal.</returns>
    private Result<Stream> Buffer(Stream package)
    {
        if (package.CanSeek)
        {
            if (package.Length == 0)
            {
                return Result<Stream>.Fail(Error.Of(nameof(ErrorMessages.IpmEmptyInput), ErrorType.Validation));
            }

            if (package.Length > _limits.MaxTotalBytes)
            {
                return Result<Stream>.Fail(Error.Of(nameof(ErrorMessages.IpmPackageTooLarge), ErrorType.Validation));
            }

            package.Position = 0;
            return Result<Stream>.Ok(package);
        }

        var copy = new MemoryStream();
        var read = CopyBounded(package, copy, _limits.MaxTotalBytes);

        if (read < 0)
        {
            return Result<Stream>.Fail(Error.Of(nameof(ErrorMessages.IpmPackageTooLarge), ErrorType.Validation));
        }

        if (read == 0)
        {
            return Result<Stream>.Fail(Error.Of(nameof(ErrorMessages.IpmEmptyInput), ErrorType.Validation));
        }

        copy.Position = 0;
        return Result<Stream>.Ok(copy);
    }

    /// <summary>Judges every entry's header, then reads each one with its size counted.</summary>
    /// <param name="zip">The opened archive.</param>
    /// <returns>The archive or the first refusal.</returns>
    private Result<IpmArchive> ReadEntries(ZipArchive zip)
    {
        var entries = zip.Entries;

        if (entries.Count == 0)
        {
            return Fail(nameof(ErrorMessages.IpmEmptyInput));
        }

        if (entries.Count > _limits.MaxEntries)
        {
            return Fail(nameof(ErrorMessages.IpmTooManyEntries));
        }

        var headerError = JudgeHeaders(entries);

        if (headerError is not null)
        {
            return Result<IpmArchive>.Fail(headerError);
        }

        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;

        foreach (var entry in entries)
        {
            var bytes = ReadEntry(entry);

            if (bytes is null)
            {
                return Fail(nameof(ErrorMessages.IpmEntrySizeMismatch));
            }

            total += bytes.Length;

            if (total > _limits.MaxTotalBytes)
            {
                return Fail(nameof(ErrorMessages.IpmPackageTooLarge));
            }

            contents.Add(entry.FullName, bytes);
        }

        if (!contents.Remove(IpmLayout.ManifestEntry, out var manifestBytes))
        {
            return Fail(nameof(ErrorMessages.IpmManifestMissing));
        }

        if (!contents.Remove(IpmLayout.SignatureEntry, out var signatureBytes))
        {
            return Fail(nameof(ErrorMessages.IpmSignatureMissing));
        }

        return Result<IpmArchive>.Ok(new IpmArchive(manifestBytes, signatureBytes, contents));
    }

    /// <summary>Judges what an entry's header declares, before a single byte of it is unpacked.</summary>
    /// <param name="entries">Every entry of the archive.</param>
    /// <returns>Null when every header is acceptable; otherwise the first refusal.</returns>
    private Error? JudgeHeaders(IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declaredTotal = 0;

        foreach (var entry in entries)
        {
            var name = entry.FullName;

            if (name.EndsWith('/') || !IsRegularFile(entry))
            {
                return Error.Of(nameof(ErrorMessages.IpmEntryNotRegular), ErrorType.Validation);
            }

            var nameError = IpmEntryNameRule.Judge(name);

            if (nameError is not null)
            {
                return nameError;
            }

            if (!exact.Add(name) || !folded.Add(name))
            {
                return Error.Of(nameof(ErrorMessages.IpmDuplicateEntry), ErrorType.Validation);
            }

            var cap = string.Equals(name, IpmLayout.ManifestEntry, StringComparison.Ordinal)
                ? _limits.MaxManifestBytes
                : _limits.MaxEntryBytes;

            if (entry.Length > cap)
            {
                return Error.Of(nameof(ErrorMessages.IpmEntryTooLarge), ErrorType.Validation);
            }

            declaredTotal += entry.Length;

            if (declaredTotal > _limits.MaxTotalBytes)
            {
                return Error.Of(nameof(ErrorMessages.IpmPackageTooLarge), ErrorType.Validation);
            }

            if (entry.Length >= _limits.RatioFloorBytes
                && entry.Length > Math.Max(entry.CompressedLength, 1) * _limits.MaxCompressionRatio)
            {
                return Error.Of(nameof(ErrorMessages.IpmRatioExceeded), ErrorType.Validation);
            }
        }

        return null;
    }

    /// <summary>
    /// True when the entry's Unix mode, if it carries one, says plain file. A symbolic link, device or
    /// directory mode is refused; an entry with no mode (made on another system) is a plain file.
    /// </summary>
    /// <param name="entry">The entry to judge.</param>
    /// <returns>True for a plain file.</returns>
    private static bool IsRegularFile(ZipArchiveEntry entry)
    {
        var fileType = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        return fileType is 0 or UnixRegularFile;
    }

    /// <summary>Unpacks one entry counting the bytes that actually come out, never the declared length.</summary>
    /// <param name="entry">The entry to read.</param>
    /// <returns>Its bytes, or null when the count differs from the declared size.</returns>
    private static byte[]? ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var read = CopyBounded(stream, buffer, entry.Length);

        return read == entry.Length ? buffer.ToArray() : null;
    }

    /// <summary>Copies at most <paramref name="limit"/> bytes and reports whether the source held more.</summary>
    /// <param name="source">The stream to read.</param>
    /// <param name="destination">Where the bytes go.</param>
    /// <param name="limit">The most bytes to accept.</param>
    /// <returns>The number of bytes copied, or -1 when the source held more than the limit.</returns>
    private static long CopyBounded(Stream source, Stream destination, long limit)
    {
        var chunk = new byte[ReadBufferBytes];
        long copied = 0;
        int read;

        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            copied += read;

            if (copied > limit)
            {
                return -1;
            }

            destination.Write(chunk, 0, read);
        }

        return copied;
    }

    /// <summary>Builds a validation failure from a code.</summary>
    /// <param name="code">The error code, a key of the resx.</param>
    /// <returns>The failed result.</returns>
    private static Result<IpmArchive> Fail(string code)
    {
        return Result<IpmArchive>.Fail(Error.Of(code, ErrorType.Validation));
    }
}
