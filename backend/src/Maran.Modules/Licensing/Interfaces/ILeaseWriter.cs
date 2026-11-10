namespace Maran.Modules.Licensing.Interfaces;

/// <summary>Stores a lease envelope the renewal has already verified. The one writer of the lease file.</summary>
public interface ILeaseWriter
{
    /// <summary>Replaces the stored lease with <paramref name="envelopeText"/>.</summary>
    /// <param name="envelopeText">The verified envelope, exactly as it was judged.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves once the file holds the new lease and no reader can have seen a mixture of old and new.</returns>
    /// <exception cref="IOException">The file could not be written; the old lease is untouched.</exception>
    Task WriteAsync(string envelopeText, CancellationToken cancellationToken);
}
