using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Tests.TestSupport;

namespace Maran.Modules.Ssl.Tests.Domain;

/// <summary>Behavioural contract of the <see cref="Maran.Modules.Ssl.Domain.Certificate"/> entity.</summary>
public sealed class CertificateTests
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The thirty-day head start the renewal job uses.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>A certificate expiring in twenty nine days is due for renewal.</summary>
    [Fact]
    public void A_certificate_expiring_in_twenty_nine_days_is_due_for_renewal()
    {
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "a.example.com", Now.AddDays(29));

        Assert.True(certificate.IsDueForRenewal(Now, Window));
    }

    /// <summary>A certificate expiring in thirty one days is not due for renewal.</summary>
    [Fact]
    public void A_certificate_expiring_in_thirty_one_days_is_not_due_for_renewal()
    {
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "b.example.com", Now.AddDays(31));

        Assert.False(certificate.IsDueForRenewal(Now, Window));
    }

    /// <summary>A certificate that has already expired is still due for renewal.</summary>
    [Fact]
    public void A_certificate_that_has_already_expired_is_still_due_for_renewal()
    {
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "c.example.com", Now.AddDays(-5));

        Assert.True(certificate.IsDueForRenewal(Now, Window));
    }

    /// <summary>A customer supplied certificate is never due for renewal.</summary>
    [Fact]
    public void A_customer_supplied_certificate_is_never_due_for_renewal()
    {
        var certificate = SslTestContext.Certificate(
            Guid.NewGuid(), "d.example.com", Now.AddDays(1), CertificateSource.Custom);

        Assert.False(certificate.IsDueForRenewal(Now, Window));
    }

    /// <summary>A failed renewal records its code and counts up without moving the expiry.</summary>
    [Fact]
    public void A_failed_renewal_records_its_code_and_counts_up_without_moving_the_expiry()
    {
        var expiry = Now.AddDays(10);
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "e.example.com", expiry);

        certificate.RenewalFailed("AcmeValidationFailed", Now);
        certificate.RenewalFailed("AcmeValidationFailed", Now.AddDays(1));

        Assert.Equal(2, certificate.ConsecutiveRenewalFailures);
        Assert.Equal("AcmeValidationFailed", certificate.LastRenewalErrorCode);
        Assert.Equal(Now.AddDays(1), certificate.LastRenewalAttemptAt);

        // The old certificate is still on disk and still serving; its real expiry is what decides
        // when the site goes dark, not the failed attempt.
        Assert.Equal(expiry, certificate.NotAfter);
    }

    /// <summary>A successful renewal clears the failure state.</summary>
    [Fact]
    public void A_successful_renewal_clears_the_failure_state()
    {
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "f.example.com", Now.AddDays(10));
        certificate.RenewalFailed("AcmeValidationFailed", Now);

        certificate.Renewed(Now.AddDays(90), Now.AddDays(2));

        Assert.Equal(0, certificate.ConsecutiveRenewalFailures);
        Assert.Equal(string.Empty, certificate.LastRenewalErrorCode);
        Assert.Equal(Now.AddDays(90), certificate.NotAfter);
        Assert.Equal(Now.AddDays(2), certificate.IssuedAt);
    }

    /// <summary>A new certificate starts with no renewal history.</summary>
    [Fact]
    public void A_new_certificate_starts_with_no_renewal_history()
    {
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "g.example.com", Now.AddDays(90));

        Assert.Null(certificate.LastRenewalAttemptAt);
        Assert.Equal(string.Empty, certificate.LastRenewalErrorCode);
        Assert.Equal(0, certificate.ConsecutiveRenewalFailures);
    }

    /// <summary>A certificate that has never failed waits for nothing.</summary>
    [Fact]
    public void A_certificate_that_has_never_failed_waits_for_nothing()
    {
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "h.example.com", Now.AddDays(10));

        Assert.Equal(DateTimeOffset.MinValue, certificate.NextAttemptAllowedAt());
        Assert.True(certificate.IsDueForRenewal(Now, Window));
    }

    /// <summary>One failure holds the next attempt off for an hour.</summary>
    [Fact]
    public void One_failure_holds_the_next_attempt_off_for_an_hour()
    {
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "i.example.com", Now.AddDays(10));
        certificate.RenewalFailed("AcmeValidationFailed", Now);

        Assert.False(certificate.IsDueForRenewal(Now.AddMinutes(59), Window));
        Assert.True(certificate.IsDueForRenewal(Now.AddHours(1), Window));
    }

    /// <summary>The wait doubles with each further failure.</summary>
    [Fact]
    public void The_wait_doubles_with_each_further_failure()
    {
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "j.example.com", Now.AddDays(10));
        certificate.RenewalFailed("AcmeValidationFailed", Now);
        certificate.RenewalFailed("AcmeValidationFailed", Now);
        certificate.RenewalFailed("AcmeValidationFailed", Now);

        // Three failures: 2^2 = four hours.
        Assert.False(certificate.IsDueForRenewal(Now.AddHours(3), Window));
        Assert.True(certificate.IsDueForRenewal(Now.AddHours(4), Window));
    }

    /// <summary>The wait is capped so a certificate never stops retrying before it expires.</summary>
    [Fact]
    public void The_wait_is_capped_so_a_certificate_never_stops_retrying_before_it_expires()
    {
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "k.example.com", Now.AddDays(10));
        for (var failure = 0; failure < 40; failure++)
        {
            certificate.RenewalFailed("AcmeValidationFailed", Now);
        }

        // Uncapped doubling would be longer than the thirty-day window after a dozen failures, and a
        // certificate that stopped retrying before its own expiry would expire while the panel
        // believed it was still trying. Forty failures also proves the exponent itself is bounded:
        // Math.Pow(2, 39) hours overflows TimeSpan and would throw rather than saturate.
        Assert.Equal(Now.AddDays(8), certificate.NextAttemptAllowedAt());
        Assert.True(certificate.IsDueForRenewal(Now.AddDays(8), Window));
    }

    /// <summary>A success clears the wait as well as the count.</summary>
    [Fact]
    public void A_success_clears_the_wait_as_well_as_the_count()
    {
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "l.example.com", Now.AddDays(10));
        certificate.RenewalFailed("AcmeValidationFailed", Now);
        certificate.RenewalFailed("AcmeValidationFailed", Now);

        certificate.Renewed(Now.AddDays(10), Now);

        Assert.Equal(DateTimeOffset.MinValue, certificate.NextAttemptAllowedAt());
    }
}
