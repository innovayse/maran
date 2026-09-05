using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// Answers the deletion's post-cascade audit with a scripted list, so a test can decide whether the
/// panel still holds rows for the account without composing every module's schema.
/// </summary>
/// <remarks>
/// The real auditor reads the composed panel's mapping, which a module's own unit test has not got.
/// What this stands in for is only the ANSWER: the behaviour under test is what the deletion does
/// with it, and that is the half the defect lived in — a deletion that carried on regardless.
/// </remarks>
public sealed class StubAccountResidueAuditor : IAccountResidueAuditor
{
    /// <summary>What the audit reports. Empty — the default — means the cascade emptied everything.</summary>
    public IReadOnlyList<string> Residue { get; init; } = [];

    /// <summary>
    /// The modules the audit could not read at all. Empty — the default — means every composed
    /// module really was asked, which is the only case in which "nothing left" is a finding rather
    /// than an absence of one.
    /// </summary>
    public IReadOnlyList<string> Unchecked { get; init; } = [];

    /// <summary>The account the audit was asked about, or null when it was never consulted.</summary>
    public Guid? Audited { get; private set; }

    /// <inheritdoc />
    public Task<AccountResidue> FindResidueAsync(Guid accountId, CancellationToken cancellationToken)
    {
        Audited = accountId;

        return Task.FromResult(new AccountResidue(Residue, Unchecked));
    }
}
