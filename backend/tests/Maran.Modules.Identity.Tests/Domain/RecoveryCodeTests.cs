using Maran.Modules.Identity.Domain;

namespace Maran.Modules.Identity.Tests.Domain;
/// <summary>Behavioural contract of recovery code.</summary>

public sealed class RecoveryCodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A new recovery code is usable.</summary>
    [Fact]
    public void A_new_recovery_code_is_usable()
    {
        Assert.True(new RecoveryCode(Guid.NewGuid(), Guid.NewGuid(), "hash").IsUsable());
    }

    /// <summary>A consumed recovery code is no longer usable.</summary>
    [Fact]
    public void A_consumed_recovery_code_is_no_longer_usable()
    {
        var code = new RecoveryCode(Guid.NewGuid(), Guid.NewGuid(), "hash");

        code.Consume(Now);

        Assert.False(code.IsUsable());
    }

    /// <summary>Consuming twice keeps the first instant.</summary>
    [Fact]
    public void Consuming_twice_keeps_the_first_instant()
    {
        var code = new RecoveryCode(Guid.NewGuid(), Guid.NewGuid(), "hash");
        code.Consume(Now);

        code.Consume(Now.AddHours(1));

        Assert.Equal(Now, code.ConsumedAt);
    }
}
