using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Answers, for one account, what a module would hand to another server (issue #127). Implemented by each
/// module that owns resources of an account, and gathered by the Migration module, which cannot reference them.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="IPersonalDataProvider"/>: every module answers one question about an account,
/// by named fields only, and a single owner gathers the answers. A module whose resources a move cannot carry
/// still answers, with what it could not carry, so the administrator is told and not surprised.
/// </para>
/// <para>
/// <b>Administrator only.</b> An exporter is reached from an administrator's request; its queries run under that
/// caller's tenant scope, so a customer or reseller caller reads nothing. It never returns a password, hash,
/// token or private key.
/// </para>
/// </remarks>
public interface IAccountMigrationExporter
{
    /// <summary>The id of the module this exporter answers for, as in its manifest.</summary>
    string ModuleId { get; }

    /// <summary>Reads what the module would hand over for one account.</summary>
    /// <param name="accountId">The account being exported.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The module's section, or <c>null</c> when it holds nothing for the account.</returns>
    Task<MigrationSection?> ExportAsync(Guid accountId, CancellationToken cancellationToken);
}
