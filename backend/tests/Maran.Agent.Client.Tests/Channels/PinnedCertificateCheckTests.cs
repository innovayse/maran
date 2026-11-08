using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Tests.TestSupport;

namespace Maran.Agent.Client.Tests.Channels;

/// <summary>The judgement of whether a node's certificate is the approved one: pin and validity, nothing else.</summary>
public sealed class PinnedCertificateCheckTests
{
    /// <summary>The exact approved certificate inside its validity period is accepted: the control for every refusal below.</summary>
    [Fact]
    public void The_pinned_certificate_inside_its_validity_is_accepted()
    {
        using var node = TestCertificates.CreateValid("node");

        Assert.True(PinnedCertificateCheck.Accepts(node, TestCertificates.Fingerprint(node), TestCertificates.Now));
    }

    /// <summary>A different certificate is refused even when its subject is identical to the approved one's.</summary>
    [Fact]
    public void A_different_certificate_with_the_same_subject_is_refused()
    {
        using var approved = TestCertificates.CreateValid("node");
        using var impostor = TestCertificates.CreateValid("node");

        Assert.False(PinnedCertificateCheck.Accepts(impostor, TestCertificates.Fingerprint(approved), TestCertificates.Now));
    }

    /// <summary>A node that presented no certificate is refused.</summary>
    [Fact]
    public void No_certificate_is_refused()
    {
        using var node = TestCertificates.CreateValid("node");

        Assert.False(PinnedCertificateCheck.Accepts(null, TestCertificates.Fingerprint(node), TestCertificates.Now));
    }

    /// <summary>The pinned certificate after its end date is refused, so expiry is a managed event and not a silent pass.</summary>
    [Fact]
    public void The_pinned_certificate_after_its_end_date_is_refused()
    {
        using var node = TestCertificates.Create("node", TestCertificates.Now.AddDays(-400), TestCertificates.Now.AddDays(-1));

        Assert.False(PinnedCertificateCheck.Accepts(node, TestCertificates.Fingerprint(node), TestCertificates.Now));
    }

    /// <summary>The pinned certificate before its start date is refused.</summary>
    [Fact]
    public void The_pinned_certificate_before_its_start_date_is_refused()
    {
        using var node = TestCertificates.Create("node", TestCertificates.Now.AddDays(1), TestCertificates.Now.AddDays(400));

        Assert.False(PinnedCertificateCheck.Accepts(node, TestCertificates.Fingerprint(node), TestCertificates.Now));
    }

    /// <summary>A pin that is not the lowercase hex form never matches: a malformed pin fails closed.</summary>
    [Fact]
    public void A_malformed_pin_never_matches()
    {
        using var node = TestCertificates.CreateValid("node");

        Assert.False(PinnedCertificateCheck.Accepts(node, "not-a-fingerprint", TestCertificates.Now));
        Assert.False(PinnedCertificateCheck.Accepts(node, string.Empty, TestCertificates.Now));
    }
}
