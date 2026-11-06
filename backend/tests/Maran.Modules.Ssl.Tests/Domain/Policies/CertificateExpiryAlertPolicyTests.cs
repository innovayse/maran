using Maran.Modules.Ssl.Domain.Policies;

namespace Maran.Modules.Ssl.Tests.Domain.Policies;

/// <summary>Behavioural contract of <see cref="CertificateExpiryAlertPolicy"/>.</summary>
public sealed class CertificateExpiryAlertPolicyTests
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The alert window is fourteen days.</summary>
    [Fact]
    public void The_alert_window_is_fourteen_days()
    {
        Assert.Equal(14, CertificateExpiryAlertPolicy.ExpiryAlertDays);
    }

    /// <summary>A certificate expiring in exactly fourteen days is due for an alert.</summary>
    [Fact]
    public void A_certificate_expiring_in_exactly_fourteen_days_is_due()
    {
        Assert.True(CertificateExpiryAlertPolicy.IsDue(Now.AddDays(14), Now));
    }

    /// <summary>A certificate expiring one tick beyond fourteen days is not due for an alert.</summary>
    [Fact]
    public void A_certificate_expiring_one_tick_beyond_fourteen_days_is_not_due()
    {
        Assert.False(CertificateExpiryAlertPolicy.IsDue(Now.AddDays(14).AddTicks(1), Now));
    }

    /// <summary>A certificate expiring in thirty days is not due for an alert.</summary>
    [Fact]
    public void A_certificate_expiring_in_thirty_days_is_not_due()
    {
        Assert.False(CertificateExpiryAlertPolicy.IsDue(Now.AddDays(30), Now));
    }

    /// <summary>A certificate that has already expired is due for an alert.</summary>
    [Fact]
    public void A_certificate_that_has_already_expired_is_due()
    {
        Assert.True(CertificateExpiryAlertPolicy.IsDue(Now.AddDays(-3), Now));
    }

    /// <summary>A certificate expiring this very instant is due for an alert.</summary>
    [Fact]
    public void A_certificate_expiring_this_instant_is_due()
    {
        Assert.True(CertificateExpiryAlertPolicy.IsDue(Now, Now));
    }
}
