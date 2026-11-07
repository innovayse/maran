using System.Globalization;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.VerifyAuditChain;

/// <summary>
/// Handles <see cref="VerifyAuditChainCommand"/> by walking the chain and journalling the verdict, whether
/// it is clean or not.
/// </summary>
/// <remarks>
/// <para>
/// The verdict is written as <see cref="IdentityAuditActions.AuditChainVerified"/>, with the entry marked
/// failed when the chain is broken. A break is not mailed to anyone: it is visible to whoever reads the
/// journal or this endpoint, and the documentation says so.
/// </para>
/// <para>
/// The subject names the first broken position and what was wrong, and never any row's content, so a
/// total break at the first position reads to a person as a changed key rather than as an attack on one
/// row.
/// </para>
/// </remarks>
public sealed class VerifyAuditChainCommandHandler
{
    /// <summary>Walks the chain.</summary>
    private readonly AuditChainVerifier _verifier;

    /// <summary>Records the verdict.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Who is asking, for the journal entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="verifier">Walks the chain.</param>
    /// <param name="journal">Records the verdict.</param>
    /// <param name="currentUser">Who is asking.</param>
    /// <param name="clock">The panel's clock, which stamps the answer.</param>
    public VerifyAuditChainCommandHandler(
        AuditChainVerifier verifier,
        IdentityAuditJournal journal,
        ICurrentUser currentUser,
        IClock clock)
    {
        _verifier = verifier;
        _journal = journal;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>Verifies the chain and journals the verdict.</summary>
    /// <param name="command">The command; it carries only the server-established origin.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The verdict, which is a success whether or not the chain held.</returns>
    public async Task<Result<AuditChainVerificationDto>> HandleAsync(
        VerifyAuditChainCommand command,
        CancellationToken cancellationToken)
    {
        var verdict = await _verifier.VerifyAsync(cancellationToken);
        var verifiedAt = _clock.UtcNow;

        await _journal.RecordIdentifiedAsync(
            SignedInCallerPolicy.Require(_currentUser).UserId,
            IdentityAuditActions.AuditChainVerified,
            SubjectFor(verdict),
            command.IpAddress,
            command.UserAgent,
            verdict.Intact,
            cancellationToken);

        return Result<AuditChainVerificationDto>.Ok(new AuditChainVerificationDto(
            verdict.Intact,
            verdict.RowsChecked,
            verdict.FirstBrokenChainIndex,
            verdict.Break.ToString(),
            verifiedAt));
    }

    /// <summary>Spells the verdict for the journal.</summary>
    /// <param name="verdict">What the walk found.</param>
    /// <returns>A short machine-readable line: how many rows, and where and how the chain broke.</returns>
    private static string SubjectFor(AuditChainVerdict verdict)
    {
        var rows = verdict.RowsChecked.ToString(CultureInfo.InvariantCulture);

        if (verdict.Intact)
        {
            return "intact rowsChecked=" + rows;
        }

        return "broken break=" + verdict.Break
            + " firstBrokenChainIndex=" + verdict.FirstBrokenChainIndex?.ToString(CultureInfo.InvariantCulture)
            + " rowsChecked=" + rows;
    }
}
