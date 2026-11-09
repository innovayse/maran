using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>
/// Gives a context that reads encrypted columns raw a model cache entry of its own, so it is not handed
/// the model another context built with a different cipher.
/// </summary>
/// <remarks>
/// EF Core builds a context's model once per context type and caches it, and the column converter holds
/// the cipher the FIRST context was built with. Without a key of its own, a second context over the same
/// database that was meant to read with the identity cipher silently reads with the first one's, and the
/// positive control of an encryption-at-rest test then reports the decrypted value as the stored one.
/// </remarks>
public sealed class RawReadModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(DbContext context, bool designTime)
    {
        return (context.GetType(), designTime, "RawRead");
    }
}
