using System.Text;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Options;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Reads the stored lease from <c>/var/lib/maran/lease.json</c> and checks its Ed25519 signature
/// against the lease key pinned in the build. The open default of <see cref="ILicenceLeaseSource"/>, and
/// the source of the signed bytes <see cref="LeaseRequestAuthorizer"/> presents to the licence service
/// (<see cref="ILeaseEnvelopeSource"/>); whatever writes the file is not part of this class.
/// </summary>
/// <remarks>
/// <para>
/// <b>The envelope</b> is judged by <see cref="LeaseEnvelopeReader"/>, the one place the lease format is read,
/// which the renewal also uses before it stores anything.
/// </para>
/// <para>
/// <b>Never throws.</b> A missing or empty file is <see cref="LeaseReading.Absent"/>; anything unreadable,
/// oversized, mistyped, with an end that does not follow its start, or wrongly signed is
/// <see cref="LeaseReading.Refused"/> with its reason. The file is bounded in size before it is read
/// (<see cref="LeaseStorageOptions.MaxLeaseFileBytes"/>), so a hostile file cannot make the panel read
/// without limit. Whose lease it is and whether its dates hold are NOT decided here but by
/// <see cref="Domain.Policies.LeaseGracePolicy"/>, which needs the licence.
/// </para>
/// </remarks>
public sealed class FileLeaseSource : ILicenceLeaseSource, ILeaseEnvelopeSource
{
    /// <summary>Where the lease file is.</summary>
    private readonly LeaseStorageOptions _options;

    /// <summary>Judges the file's text: the envelope, the signature and the payload.</summary>
    private readonly LeaseEnvelopeReader _reader;

    /// <summary>Creates the source.</summary>
    /// <param name="options">Where the lease file is.</param>
    /// <param name="signatureVerifier">Checks the lease's signature.</param>
    public FileLeaseSource(IOptions<LeaseStorageOptions> options, ILeaseSignatureVerifier signatureVerifier)
    {
        _options = options.Value;
        _reader = new LeaseEnvelopeReader(signatureVerifier);
    }

    /// <inheritdoc />
    public async Task<LeaseReading> ReadAsync(CancellationToken cancellationToken)
    {
        return (await ReadFileAsync(cancellationToken)).Reading;
    }

    /// <inheritdoc />
    public async Task<SignedLeasePayload?> ReadSignedAsync(CancellationToken cancellationToken)
    {
        var file = await ReadFileAsync(cancellationToken);

        // Only a lease whose signature held has an envelope to hand over; the reading and the envelope
        // come from one read of one file, so they cannot describe two different leases.
        return file.Reading is LeaseReading.Present ? file.Verified : null;
    }

    /// <summary>Reads the file once and judges it, keeping the signed bytes beside the reading.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The reading, and the signed payload when the reading is a verified lease.</returns>
    private async Task<LeaseEnvelopeReading> ReadFileAsync(CancellationToken cancellationToken)
    {
        string text;
        try
        {
            var info = new FileInfo(_options.LeaseFilePath);
            if (!info.Exists)
            {
                return LeaseEnvelopeReading.Of(LeaseReading.Absent.Instance);
            }

            if (info.Length > LeaseStorageOptions.MaxLeaseFileBytes)
            {
                return LeaseEnvelopeReading.Of(new LeaseReading.Refused(LeaseRefusalReason.Malformed));
            }

            text = await File.ReadAllTextAsync(_options.LeaseFilePath, Encoding.UTF8, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable file is "no lease": the same direction the licence file's source takes, and
            // the safe one, because absence never grants cover.
            return LeaseEnvelopeReading.Of(LeaseReading.Absent.Instance);
        }

        return string.IsNullOrWhiteSpace(text) ? LeaseEnvelopeReading.Of(LeaseReading.Absent.Instance) : _reader.Read(text);
    }
}
