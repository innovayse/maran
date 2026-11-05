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
}
