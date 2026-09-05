using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Models;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IAcmeClient"/> double that throws for one named domain and succeeds for the rest.
/// </summary>
/// <remarks>
/// The renewal job reaches an <c>HttpClient</c>, a <c>Task.Delay</c> and the database, so a throw is
/// a real possibility rather than a hypothetical — and an uncaught one used to end the pass, taking
/// with it the row updates of every certificate already renewed before it.
/// </remarks>
public sealed class ThrowingAcmeClient : IAcmeClient
{
    /// <summary>The domain whose order throws.</summary>
    private readonly string _throwingDomain;

    /// <summary>The material every other order produces.</summary>
    private readonly IssuedCertificate _material;

    /// <summary>Every order this client was asked to place, in order.</summary>
    public List<AcmeOrderRequest> Orders { get; } = [];

    /// <summary>Creates a client that throws for one domain.</summary>
    /// <param name="throwingDomain">The domain whose order throws.</param>
    /// <param name="material">The material every other order produces.</param>
    public ThrowingAcmeClient(string throwingDomain, IssuedCertificate material)
    {
        _throwingDomain = throwingDomain;
        _material = material;
    }

    /// <inheritdoc />
    public Task<Result<IssuedCertificate>> OrderAsync(
        AcmeOrderRequest request,
        CancellationToken cancellationToken)
    {
        Orders.Add(request);

        if (string.Equals(request.Domain, _throwingDomain, StringComparison.Ordinal))
        {
            throw new HttpRequestException("the authority is unreachable");
        }

        return Task.FromResult(Result<IssuedCertificate>.Ok(_material));
    }
}
