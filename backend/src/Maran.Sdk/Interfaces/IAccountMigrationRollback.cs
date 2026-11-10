namespace Maran.Sdk.Interfaces;

/// <summary>
/// Deletes the account an import created when the import fails, and refuses to delete any other (issue #127).
/// Implemented by the module that owns the accounts.
/// </summary>
/// <remarks>
/// <para>
/// A failed import deletes the half-built account through the ordinary deletion cascade (every module releases
/// its rows on <c>AccountDeleting</c>), skipping the final backup: nothing worth keeping exists yet, and a
/// backup of a half-import would be noise.
/// </para>
/// <para>
/// <b>It must never delete an account the import did not create.</b> The caller names the account it recorded
/// and the moment its job started; an account that existed before that moment is refused, so a defect or a
/// forged id upstream cannot turn a rollback into the deletion of a customer.
/// </para>
/// </remarks>
public interface IAccountMigrationRollback
{
    /// <summary>Deletes an account the import created.</summary>
    /// <param name="accountId">The account the import recorded as its own.</param>
    /// <param name="importStartedAt">When the import's job started; the account must have been created at or after it.</param>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>Success when the account is gone, otherwise the typed failure; a refusal means the account remains.</returns>
    Task<Result> DeleteImportedAccountAsync(Guid accountId, DateTimeOffset importStartedAt, CancellationToken cancellationToken);
}
