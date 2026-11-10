using Maran.Modules.Ssl.Mappers;
using Maran.Modules.Ssl.Tests.TestSupport;

namespace Maran.Modules.Ssl.Tests.Mappers;

/// <summary>The mapper restates the row and the sentence it is handed, and decides nothing.</summary>
public sealed class CertificateMapperTests
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A certificate with no failure carries no sentence.</summary>
    [Fact]
    public void A_certificate_with_no_failure_carries_no_sentence()
    {
        var dto = CertificateMapper.From(SslTestContext.Certificate(Guid.NewGuid(), "a.example.com", Now), null);

        Assert.Null(dto.LastRenewalError);
        Assert.Null(dto.LastRenewalErrorCode);
    }

    /// <summary>A failing certificate carries its code and the sentence the caller resolved.</summary>
    [Fact]
    public void A_failing_certificate_carries_its_code_and_the_sentence_the_caller_resolved()
    {
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "a.example.com", Now);
        certificate.RenewalFailed("AcmeAuthorityUnreachable", Now);

        var dto = CertificateMapper.From(certificate, "The authority could not be reached.");

        Assert.Equal("AcmeAuthorityUnreachable", dto.LastRenewalErrorCode);
        Assert.Equal("The authority could not be reached.", dto.LastRenewalError);
        Assert.Equal(1, dto.ConsecutiveRenewalFailures);
    }

    /// <summary>A wildcard certificate is reported as one and a plain one is not.</summary>
    [Fact]
    public void A_wildcard_certificate_is_reported_as_one_and_a_plain_one_is_not()
    {
        var wildcard = SslTestContext.Certificate(Guid.NewGuid(), "a.example.com", Now, isWildcard: true);
        var plain = SslTestContext.Certificate(Guid.NewGuid(), "b.example.com", Now);

        Assert.True(CertificateMapper.From(wildcard, null).Wildcard);
        Assert.False(CertificateMapper.From(plain, null).Wildcard);
    }
}
