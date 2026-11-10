namespace Maran.Modules.ServerMigration.Interfaces;

/// <summary>
/// The one place a grant is spent (issue #127): counting a read of the manifest and taking the single read of
/// the artifact, each as an atomic act against the stored row.
/// </summary>
/// <remarks>
/// A seam so the pull commands are tested without a database that can hold a row lock; the real one
/// (<see cref="Services.ExportGrantLedger"/>) is exercised against PostgreSQL.
/// </remarks>
public interface IExportGrantLedger
{
    /// <summary>Counts one read of the manifest (the head of the package).</summary>
    /// <param name="grantId">The grant that was authenticated.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>true</c> when the read is allowed and was counted; <c>false</c> when it is not.</returns>
    Task<bool> CountManifestReadAsync(Guid grantId, CancellationToken cancellationToken);

    /// <summary>Takes the single read of the artifact, at its first byte.</summary>
    /// <param name="grantId">The grant that was authenticated.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <c>true</c> for exactly one caller of a live grant, however many ask at once; <c>false</c> for every other.
    /// </returns>
    Task<bool> TakeArtifactReadAsync(Guid grantId, CancellationToken cancellationToken);
}
