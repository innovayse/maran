using System.Security.Cryptography;
using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Options;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Streams a granted package into a private temp file and proves it is the package the signed catalogue
/// announced, before anything is handed to the loader. It checks what it can without a byte of the
/// package, then what only the bytes can tell.
/// </summary>
/// <remarks>
/// <para>
/// <b>Before any request:</b> the grant's address must be on the configured host
/// (<c>GrantHostRefused</c>), the grant must not have expired, and the grant's size and digest must be the
/// ones the catalogue declared and the size must be within <c>MaxPackageBytes</c>. A grant that disagrees
/// with the signed catalogue is a mirror or a middlebox talking, and no connection is opened for it.
/// </para>
/// <para>
/// <b>While streaming:</b> the file is created exclusively with owner-only permissions in the configured
/// package directory under a random name (never a name taken from a response), SHA-256 is computed as the
/// bytes arrive, and the read stops one byte past the declared size, so a host that sends more than it
/// promised never fills the disk. Each read has its own stall deadline, so a slow link is fine and a dead
/// one is not.
/// </para>
/// <para>
/// <b>Afterwards:</b> the byte count must equal the declared size and the digest must equal the declared
/// digest. On any failure, including cancellation, the file is deleted before the failure is returned. The
/// package's own signature and its contents are the loader's business; this type never opens the archive.
/// </para>
/// </remarks>
public sealed class PackageDownloader
{
    /// <summary>The size of one read from the network and one write to the file.</summary>
    private const int BufferBytes = 81920;

    /// <summary>The length of a SHA-256 digest in bytes.</summary>
    private const int DigestBytes = 32;

    /// <summary>Pre-compiled log delegate for a partial file that could not be removed.</summary>
    private static readonly Action<ILogger, string, Exception?> LogCouldNotRemove =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogCouldNotRemove)),
            "A partial marketplace download could not be removed: {Path}");

    /// <summary>The options reader.</summary>
    private readonly MarketplaceOptionsReader _options;

    /// <summary>The time source.</summary>
    private readonly IClock _clock;

    /// <summary>Operator log.</summary>
    private readonly ILogger<PackageDownloader> _logger;

    /// <summary>Creates the downloader.</summary>
    /// <param name="options">The options reader.</param>
    /// <param name="clock">The time source.</param>
    /// <param name="logger">Operator log.</param>
    public PackageDownloader(MarketplaceOptionsReader options, IClock clock, ILogger<PackageDownloader> logger)
    {
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Downloads the package a grant names and proves it is the announced one.</summary>
    /// <param name="transport">The transport to download through (the disabled one refuses).</param>
    /// <param name="grant">The verified single-use grant.</param>
    /// <param name="expected">What the signed catalogue declared for this package.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The downloaded package, which the caller disposes; or a coded failure with no file left behind.</returns>
    public async Task<Result<DownloadedPackageSource>> DownloadAsync(
        IMarketplaceTransport transport,
        DownloadGrant grant,
        ExpectedModulePackage expected,
        CancellationToken ct)
    {
        var settings = _options.Read();
        if (!settings.IsSuccess)
        {
            return Result<DownloadedPackageSource>.Fail(settings.Error!);
        }

        var options = settings.Value;
        var baseUri = MarketplaceHostPolicy.BaseUri(options);

        if (!Uri.TryCreate(grant.Url.Reveal(), UriKind.Absolute, out var url)
            || !MarketplaceHostPolicy.IsConfiguredHost(url, baseUri))
        {
            return Result<DownloadedPackageSource>.Fail(MarketplaceClientRefusals.GrantHostRefused);
        }

        var agreed = CheckAgreement(grant, expected, options);
        if (!agreed.IsSuccess)
        {
            return Result<DownloadedPackageSource>.Fail(agreed.Error!);
        }

        var opened = await transport.OpenDownloadAsync(url, ct);
        if (!opened.IsSuccess)
        {
            return Result<DownloadedPackageSource>.Fail(opened.Error!);
        }

        using var download = opened.Value;
        if (download.ContentLength is { } declared && declared != expected.SizeBytes)
        {
            return Result<DownloadedPackageSource>.Fail(MarketplaceClientRefusals.DownloadSizeMismatch);
        }

        var directory = PrepareDirectory(options.PackageDirectory);
        if (directory is null)
        {
            return Result<DownloadedPackageSource>.Fail(MarketplaceClientRefusals.StorageUnavailable);
        }

        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.ipm");
        var kept = false;

        try
        {
            var stored = await StoreAsync(
                download.Body, path, expected, TimeSpan.FromSeconds(options.RequestTimeoutSeconds), ct);
            if (!stored.IsSuccess)
            {
                return Result<DownloadedPackageSource>.Fail(stored.Error!);
            }

            kept = true;
            return Result<DownloadedPackageSource>.Ok(
                new DownloadedPackageSource(path, expected, $"marketplace {baseUri.IdnHost}"));
        }
        catch (IOException)
        {
            return Result<DownloadedPackageSource>.Fail(MarketplaceClientRefusals.StorageUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            return Result<DownloadedPackageSource>.Fail(MarketplaceClientRefusals.StorageUnavailable);
        }
        finally
        {
            if (!kept)
            {
                TryDelete(path);
            }
        }
    }

    /// <summary>Tells whether a hexadecimal text is a SHA-256 digest, and reads it.</summary>
    /// <param name="hex">The text.</param>
    /// <param name="digest">Receives the 32 bytes.</param>
    /// <returns>True when the text is exactly 64 hexadecimal characters.</returns>
    private static bool TryReadDigest(string hex, Span<byte> digest)
    {
        if (hex.Length != DigestBytes * 2)
        {
            return false;
        }

        try
        {
            return Convert.FromHexString(hex).AsSpan().TryCopyTo(digest);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Checks the grant against the signed catalogue's declaration and the configured ceiling, with no network.</summary>
    /// <param name="grant">The grant.</param>
    /// <param name="expected">What the catalogue declared.</param>
    /// <param name="options">The validated options.</param>
    /// <returns>Success, or the size or digest refusal.</returns>
    private Result CheckAgreement(DownloadGrant grant, ExpectedModulePackage expected, MarketplaceOptions options)
    {
        if (grant.ExpiresAt <= _clock.UtcNow)
        {
            return Result.Fail(MarketplaceClientRefusals.ResponseInvalid);
        }

        if (grant.SizeBytes != expected.SizeBytes
            || expected.SizeBytes < 1
            || expected.SizeBytes > options.MaxPackageBytes)
        {
            return Result.Fail(MarketplaceClientRefusals.DownloadSizeMismatch);
        }

        Span<byte> fromGrant = stackalloc byte[DigestBytes];
        Span<byte> fromCatalogue = stackalloc byte[DigestBytes];

        return TryReadDigest(grant.Sha256, fromGrant)
            && TryReadDigest(expected.Sha256, fromCatalogue)
            && CryptographicOperations.FixedTimeEquals(fromGrant, fromCatalogue)
            ? Result.Ok()
            : Result.Fail(MarketplaceClientRefusals.DownloadDigestMismatch);
    }

    /// <summary>Makes sure the package directory exists, owner-only when it has to be created.</summary>
    /// <param name="path">The configured directory.</param>
    /// <returns>The directory, or null when it cannot be created or used.</returns>
    private static string? PrepareDirectory(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Unreachable in production, the panel ships on Linux only, but the mode overload is unsupported there.
                Directory.CreateDirectory(path);
            }
            else
            {
                Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return path;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Streams the body into a new owner-only file while counting and hashing it, then checks size and digest.</summary>
    /// <param name="body">The download body.</param>
    /// <param name="path">The file to create; it must not exist.</param>
    /// <param name="expected">The declared size and digest.</param>
    /// <param name="stall">How long one read may wait for data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the size, digest or unreachable failure. The file is left for the caller to delete.</returns>
    private static async Task<Result> StoreAsync(
        Stream body,
        string path,
        ExpectedModulePackage expected,
        TimeSpan stall,
        CancellationToken ct)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            BufferSize = BufferBytes,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var file = new FileStream(path, options);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferBytes];
        long total = 0;

        while (true)
        {
            // One byte past the declared size is enough to know the host sent more than it promised.
            var wanted = (int)Math.Min(buffer.Length, expected.SizeBytes + 1 - total);

            int read;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(stall);

                try
                {
                    read = await body.ReadAsync(buffer.AsMemory(0, wanted), deadline.Token);
                }
                catch (HttpRequestException)
                {
                    return Result.Fail(MarketplaceClientRefusals.Unreachable);
                }
                catch (IOException)
                {
                    return Result.Fail(MarketplaceClientRefusals.Unreachable);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return Result.Fail(MarketplaceClientRefusals.Unreachable);
                }
            }

            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > expected.SizeBytes)
            {
                return Result.Fail(MarketplaceClientRefusals.DownloadSizeMismatch);
            }

            hash.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        if (total != expected.SizeBytes)
        {
            return Result.Fail(MarketplaceClientRefusals.DownloadSizeMismatch);
        }

        await file.FlushAsync(ct);

        var declared = new byte[DigestBytes];
        return TryReadDigest(expected.Sha256, declared)
            && CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), declared)
            ? Result.Ok()
            : Result.Fail(MarketplaceClientRefusals.DownloadDigestMismatch);
    }

    /// <summary>Deletes a partial file, logging when it cannot.</summary>
    /// <param name="path">The file.</param>
    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            LogCouldNotRemove(_logger, path, null);
        }
        catch (UnauthorizedAccessException)
        {
            LogCouldNotRemove(_logger, path, null);
        }
    }
}
