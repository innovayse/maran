using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>The rules <see cref="ResellerEnvelope"/> holds itself to when it is stated.</summary>
public sealed class ResellerEnvelopeTests
{
    /// <summary>An envelope states exactly the figures it was given.</summary>
    [Fact]
    public void An_envelope_states_exactly_the_figures_it_was_given()
    {
        var envelope = ResellerEnvelope.Of(
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, AccountFeature.Mail, RuntimeLimits.Unlimited());

        Assert.Equal(1, envelope.MaxAccounts);
        Assert.Equal(2, envelope.DiskPoolMb);
        Assert.Equal(3, envelope.MaxPlans);
        Assert.Equal(4, envelope.MaxDiskQuotaMb);
        Assert.Equal(5, envelope.MaxSites);
        Assert.Equal(6, envelope.MaxDatabases);
        Assert.Equal(7, envelope.MaxSftpUsers);
        Assert.Equal(8, envelope.MaxFtpUsers);
        Assert.Equal(9, envelope.MaxCronEntries);
        Assert.Equal(10, envelope.MaxPhpWorkersPerPool);
        Assert.Equal(AccountFeature.Mail, envelope.Features);
    }

    /// <summary>Zero is a legitimate figure everywhere: an envelope may sell nothing.</summary>
    [Fact]
    public void Zero_is_accepted_for_every_figure()
    {
        var envelope = ResellerEnvelope.Of(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, AccountFeature.None, RuntimeLimits.Unlimited());

        Assert.Equal(0, envelope.MaxAccounts);
        Assert.Equal(0, envelope.DiskPoolMb);
    }

    /// <summary>A negative figure is refused, whichever figure it is.</summary>
    /// <param name="position">Which of the ten figures, counted from zero, is made negative.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void A_negative_figure_is_refused(int position)
    {
        var figures = new int[10];
        figures[position] = -1;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return ResellerEnvelope.Of(
                figures[0],
                figures[1],
                figures[2],
                figures[3],
                figures[4],
                figures[5],
                figures[6],
                figures[7],
                figures[8],
                figures[9],
                AccountFeature.None,
                RuntimeLimits.Unlimited());
        });
    }

    /// <summary>A feature set holding a bit that is no feature is refused.</summary>
    [Fact]
    public void A_feature_set_with_an_unknown_bit_is_refused()
    {
        var unknown = (AccountFeature)(1L << 40);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return ResellerTestData.Envelope(features: unknown);
        });
    }

    /// <summary>Null runtime ceilings are refused.</summary>
    [Fact]
    public void Null_runtime_limits_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            return ResellerEnvelope.Of(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, AccountFeature.None, null!);
        });
    }

    /// <summary>The envelope keeps its own copy of the ceilings, so the caller's instance is not shared.</summary>
    [Fact]
    public void The_envelope_keeps_a_copy_of_the_runtime_limits_it_was_given()
    {
        var given = RuntimeLimits.Of(
            ResourceLimit.Limited(100),
            ResourceLimit.Unlimited(),
            ResourceLimit.Limited(50));

        var envelope = ResellerTestData.Envelope(runtimeLimits: given);

        Assert.NotSame(given, envelope.RuntimeLimits);
        Assert.Equal(given, envelope.RuntimeLimits);
    }
}
