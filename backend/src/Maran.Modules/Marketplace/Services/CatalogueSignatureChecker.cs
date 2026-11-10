using System.Security.Cryptography;
using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Models;
using Maran.SharedKernel.Utilities.Signatures;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Decides whether a marketplace answer may be believed: that it carries a detached signature a trusted
/// key made over the exact response bytes, and that it is recent enough to still be true.
/// </summary>
/// <remarks>
/// <para>
/// <b>The trusted set</b> is the pinned key file the panel ships with (<c>MetadataKeyPath</c>, one or more
/// PEM <c>PUBLIC KEY</c> blocks, all ECDSA P-384) plus any key accepted since start because an
/// already-trusted key signed its announcement (<see cref="AcceptRotation"/>). The file is read on every
/// verification, so installing the key later needs no restart. A missing, unreadable or malformed file
/// is <c>CatalogueKeyMissing</c> and fails closed: nothing is believed without a key. Any block in the file
/// that is not a P-384 public key makes the whole file unusable, so a typo cannot silently shrink the set.
/// </para>
/// <para>
/// <b>Rotation</b> cannot be bootstrapped from the network. An announcement signed only by the key it
/// announces proves nothing (anyone can sign with a key they made up), so it is verified against the
/// keys trusted BEFORE it, and a list that fails that check is refused whole.
/// </para>
/// <para>
/// <b>Freshness</b> is judged with the injected clock: signed longer ago than <c>CatalogueMaxAgeHours</c>,
/// signed in the future by more than a few minutes' skew (which would defeat the age check), or past its
/// own <c>validUntil</c> is stale. A correct signature on an old answer is exactly the replay this refuses.
/// </para>
/// Registered as a singleton: the accepted rotation keys live in memory and are forgotten at restart,
/// after which the pinned file is the only root again.
/// </remarks>
public sealed class CatalogueSignatureChecker
{
    /// <summary>The largest pinned key file read, in bytes. A key list is a few hundred bytes.</summary>
    private const long MaxKeyFileBytes = 64 * 1024;

    /// <summary>The longest signature header accepted, in characters (a P-384 DER signature is under 140).</summary>
    private const int MaxSignatureHeaderChars = 256;

    /// <summary>The most keys an announced list may add at once.</summary>
    private const int MaxAnnouncedKeys = 8;

    /// <summary>The PEM label a public key block carries.</summary>
    private const string PublicKeyLabel = "PUBLIC KEY";

    /// <summary>How far in the future a <c>signedAt</c> may be before it is treated as a defeated age check.</summary>
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>The options reader.</summary>
    private readonly MarketplaceOptionsReader _options;

    /// <summary>The time source.</summary>
    private readonly IClock _clock;

    /// <summary>Guards <see cref="_rotated"/>.</summary>
    private readonly object _gate = new();

    /// <summary>Keys accepted by rotation since start, as SubjectPublicKeyInfo bytes.</summary>
    private readonly List<byte[]> _rotated = [];

    /// <summary>Creates the checker.</summary>
    /// <param name="options">The options reader.</param>
    /// <param name="clock">The time source.</param>
    public CatalogueSignatureChecker(MarketplaceOptionsReader options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    /// <summary>Verifies a detached signature over the exact response bytes.</summary>
    /// <param name="body">The response bytes, as received.</param>
    /// <param name="signature">The <c>X-Maran-Signature</c> header: base64 of a DER ECDSA P-384 / SHA-384 signature.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success; <c>CatalogueKeyMissing</c> when there is no usable pinned key; <c>CatalogueUnsigned</c> when
    /// the header is absent or malformed or no trusted key made the signature.
    /// </returns>
    public async Task<Result> VerifyAsync(ReadOnlyMemory<byte> body, string? signature, CancellationToken ct)
    {
        var trusted = await LoadTrustedAsync(ct);
        if (!trusted.IsSuccess)
        {
            return Result.Fail(trusted.Error!);
        }

        var der = DecodeSignature(signature);
        if (der is null)
        {
            return Result.Fail(MarketplaceClientRefusals.CatalogueUnsigned);
        }

        foreach (var key in trusted.Value.Pinned.Concat(trusted.Value.Rotated))
        {
            if (EcdsaP384Signature.Verify(key, body.Span, der))
            {
                return Result.Ok();
            }
        }

        return Result.Fail(MarketplaceClientRefusals.CatalogueUnsigned);
    }

    /// <summary>Judges whether a verified document is recent enough to be believed.</summary>
    /// <param name="document">The document, whose signature was already verified.</param>
    /// <returns>Success, or <c>CatalogueStale</c>.</returns>
    public Result CheckFreshness(ISignedMarketplaceDocument document)
    {
        var settings = _options.Read();
        if (!settings.IsSuccess)
        {
            return Result.Fail(settings.Error!);
        }

        var now = _clock.UtcNow;
        var maxAge = TimeSpan.FromHours(settings.Value.CatalogueMaxAgeHours);

        var stale = document.SignedAt > now + MaxClockSkew
            || now - document.SignedAt > maxAge
            || now > document.ValidUntil;

        return stale ? Result.Fail(MarketplaceClientRefusals.CatalogueStale) : Result.Ok();
    }

    /// <summary>Counts the keys currently trusted.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The counts, or <c>CatalogueKeyMissing</c> when the pinned file is unusable.</returns>
    public async Task<Result<MarketplaceKeyStatus>> GetStatusAsync(CancellationToken ct)
    {
        var trusted = await LoadTrustedAsync(ct);
        if (!trusted.IsSuccess)
        {
            return Result<MarketplaceKeyStatus>.Fail(trusted.Error!);
        }

        return Result<MarketplaceKeyStatus>.Ok(
            new MarketplaceKeyStatus(trusted.Value.Pinned.Count, trusted.Value.Rotated.Count));
    }

    /// <summary>
    /// Trusts the keys of an announced list. The caller has already verified the list's signature against
    /// the keys trusted before it; this only reads the keys and refuses a list that is not all valid
    /// P-384 public keys.
    /// </summary>
    /// <param name="announced">The verified key list.</param>
    /// <returns>Success, or <c>MarketplaceResponseInvalid</c> when any entry is not a P-384 public key or the list is too long.</returns>
    public Result AcceptRotation(MarketplaceKeySet announced)
    {
        if (announced.Keys.Count > MaxAnnouncedKeys)
        {
            return Result.Fail(MarketplaceClientRefusals.ResponseInvalid);
        }

        var accepted = new List<byte[]>();
        foreach (var entry in announced.Keys)
        {
            var key = EcdsaP384Signature.TryReadPem(entry.PublicKeyPem);
            if (key is null)
            {
                return Result.Fail(MarketplaceClientRefusals.ResponseInvalid);
            }

            accepted.Add(key);
        }

        lock (_gate)
        {
            foreach (var key in accepted)
            {
                if (!_rotated.Any(known => { return known.AsSpan().SequenceEqual(key); }))
                {
                    _rotated.Add(key);
                }
            }
        }

        return Result.Ok();
    }

    /// <summary>Decodes the signature header, refusing anything that cannot be a P-384 DER signature.</summary>
    /// <param name="header">The header value.</param>
    /// <returns>The DER bytes, or null.</returns>
    private static byte[]? DecodeSignature(string? header)
    {
        if (string.IsNullOrWhiteSpace(header) || header.Length > MaxSignatureHeaderChars)
        {
            return null;
        }

        var buffer = new byte[MaxSignatureHeaderChars];
        return Convert.TryFromBase64String(header.Trim(), buffer, out var written) && written > 0
            ? buffer[..written]
            : null;
    }

    /// <summary>Reads the pinned file and adds the rotation keys accepted since start.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The trusted keys, or <c>CatalogueKeyMissing</c>.</returns>
    private async Task<Result<TrustedKeys>> LoadTrustedAsync(CancellationToken ct)
    {
        var settings = _options.Read();
        if (!settings.IsSuccess)
        {
            return Result<TrustedKeys>.Fail(settings.Error!);
        }

        var pinned = await ReadPinnedAsync(settings.Value.MetadataKeyPath, ct);
        if (pinned is null)
        {
            return Result<TrustedKeys>.Fail(MarketplaceClientRefusals.CatalogueKeyMissing);
        }

        lock (_gate)
        {
            return Result<TrustedKeys>.Ok(new TrustedKeys(pinned, _rotated.ToList()));
        }
    }

    /// <summary>Reads every public key block of the pinned file.</summary>
    /// <param name="path">The pinned key file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The keys as SubjectPublicKeyInfo, or null when the file is absent, unreadable, holds no key, or holds anything but P-384 public keys.</returns>
    private static async Task<List<byte[]>?> ReadPinnedAsync(string path, CancellationToken ct)
    {
        string text;

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0 || file.Length > MaxKeyFileBytes)
            {
                return null;
            }

            text = await File.ReadAllTextAsync(path, ct);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var keys = new List<byte[]>();
        var rest = text.AsSpan();

        while (PemEncoding.TryFind(rest, out var fields))
        {
            if (!rest[fields.Label].SequenceEqual(PublicKeyLabel))
            {
                return null;
            }

            var key = EcdsaP384Signature.TryReadPem(rest[fields.Location].ToString());
            if (key is null)
            {
                return null;
            }

            keys.Add(key);
            rest = rest[fields.Location.End..];
        }

        return keys.Count == 0 ? null : keys;
    }

    /// <summary>The trusted keys at one moment: the pinned ones and the ones rotation added.</summary>
    /// <param name="Pinned">Keys from the pinned file.</param>
    /// <param name="Rotated">Keys accepted by rotation.</param>
    private sealed record TrustedKeys(IReadOnlyList<byte[]> Pinned, IReadOnlyList<byte[]> Rotated);
}
