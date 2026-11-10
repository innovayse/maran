using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Tells the administrator which certificates of a moved account have to be issued or uploaded again, and
/// creates none (issue #127, decision D2). The Ssl module's <see cref="IAccountMigrationImporter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is created, on purpose.</b> A certificate's private key never leaves its server, so there is
/// nothing to import, and an ACME certificate can be issued here only after the name points at this server
/// (the HTTP-01 check reaches the web server that answers for it). The dry run therefore states, per
/// certificate, what the administrator does after the move: issue it again for an ACME one, upload it again for
/// a custom one.
/// </para>
/// <para>
/// It runs last, after the sites that the certificates would be installed on exist.
/// </para>
/// </remarks>
public sealed class SslMigrationImporter : IAccountMigrationImporter
{
    /// <inheritdoc />
    public string ModuleId
    {
        get { return SslManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public int Order
    {
        get { return 70; }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MigrationFinding>> PlanAsync(
        MigrationSection section,
        MigrationPlanContext context,
        CancellationToken cancellationToken)
    {
        var payload = section.ReadPayload<SslMigrationSection>(ModuleId, SslMigrationExporter.SectionVersion);

        if (payload is null)
        {
            IReadOnlyList<MigrationFinding> invalid =
                [new MigrationFinding(ModuleId, MigrationFindingSeverity.Conflict, MigrationCodes.SectionInvalid, context.AccountName)];

            return Task.FromResult(invalid);
        }

        IReadOnlyList<MigrationFinding> findings = payload.Certificates
            .Select(certificate =>
            {
                var code = string.Equals(certificate.Source, nameof(CertificateSource.Acme), StringComparison.Ordinal)
                    ? MigrationCodes.CertificateReissue
                    : MigrationCodes.CertificateUpload;

                return new MigrationFinding(ModuleId, MigrationFindingSeverity.Info, code, certificate.Domain);
            })
            .ToList();

        return Task.FromResult(findings);
    }

    /// <inheritdoc />
    public Task<Result<MigrationSectionOutcome>> ImportAsync(
        MigrationSection section,
        MigrationImportTarget target,
        CancellationToken cancellationToken)
    {
        var payload = section.ReadPayload<SslMigrationSection>(ModuleId, SslMigrationExporter.SectionVersion);

        if (payload is null)
        {
            return Task.FromResult(
                Result<MigrationSectionOutcome>.Fail(Error.Of(MigrationCodes.SectionInvalid, ErrorType.Validation)));
        }

        if (target.AccountId is null)
        {
            return Task.FromResult(
                Result<MigrationSectionOutcome>.Fail(Error.Of(MigrationCodes.TargetAccountMissing, ErrorType.Failure)));
        }

        // Nothing to create: the dry run already told the administrator what to issue or upload.
        return Task.FromResult(Result<MigrationSectionOutcome>.Ok(new MigrationSectionOutcome(null, [])));
    }
}
