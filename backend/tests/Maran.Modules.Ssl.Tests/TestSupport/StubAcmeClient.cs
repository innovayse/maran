using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Models;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IAcmeClient"/> double: answers with prepared material, or with a prepared refusal,
/// and records every order it was asked to place.
/// </summary>
/// <remarks>
/// Issuance is the one step that talks to a third party over the internet, so every handler test
/// drives it through this. The orders list is what proves the handler asked for the right domain
/// under the right SYSTEM account — getting that pair wrong writes a challenge file into somebody
/// else's home, which no assertion on the returned certificate would notice.
/// </remarks>
public sealed class StubAcmeClient : IAcmeClient
{
    /// <summary>The refusal to answer with, or null to answer with <see cref="Material"/>.</summary>
    private readonly Error? _failure;

    /// <summary>Every order this client was asked to place, in order.</summary>
    public List<AcmeOrderRequest> Orders { get; } = [];

    /// <summary>The material a successful order produces.</summary>
    public IssuedCertificate Material { get; set; } =
        new("-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----", "key-material",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>Creates a client that succeeds, or one that always refuses with <paramref name="failure"/>.</summary>
    /// <param name="failure">The refusal to answer with, or null to succeed.</param>
    public StubAcmeClient(Error? failure = null)
    {
        _failure = failure;
    }

    /// <inheritdoc />
    public Task<Result<IssuedCertificate>> OrderAsync(
        AcmeOrderRequest request,
        CancellationToken cancellationToken)
    {
        Orders.Add(request);

        return Task.FromResult(_failure is null
            ? Result<IssuedCertificate>.Ok(Material)
            : Result<IssuedCertificate>.Fail(_failure));
    }
}
