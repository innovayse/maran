using Maran.Host.Configuration;
using Maran.Host.RateLimiting;
using Microsoft.Extensions.Options;

namespace Maran.Host.Tests.RateLimiting;

/// <summary>An address that keeps presenting wrong grants is locked out, and a quiet one is not (issue #127).</summary>
public sealed class MigrationGrantFailureLimiterTests
{
    /// <summary>The moment the failures below are counted.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A limiter that locks out after three failures for sixty seconds.</summary>
    /// <returns>The limiter.</returns>
    private static MigrationGrantFailureLimiter Limiter()
    {
        return new MigrationGrantFailureLimiter(new OptionsWrapper<RateLimitOptions>(
            new RateLimitOptions { LoginMaxAttempts = 3, LoginLockoutSeconds = 60 }));
    }

    /// <summary>The refusal and its accepting control: the third failure locks the address out, two do not, and another address is untouched.</summary>
    [Fact]
    public void An_address_is_locked_out_after_its_allowed_failures_and_only_that_address()
    {
        var limiter = Limiter();

        limiter.RecordFailure("198.51.100.7", Now);
        limiter.RecordFailure("198.51.100.7", Now);
        Assert.False(limiter.IsLockedOut("198.51.100.7", Now));

        limiter.RecordFailure("198.51.100.7", Now);
        Assert.True(limiter.IsLockedOut("198.51.100.7", Now));
        Assert.False(limiter.IsLockedOut("198.51.100.8", Now));
    }

    /// <summary>The lockout ends when the window that began with the first failure has passed.</summary>
    [Fact]
    public void The_lockout_ends_with_its_window()
    {
        var limiter = Limiter();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            limiter.RecordFailure("198.51.100.7", Now);
        }

        Assert.True(limiter.IsLockedOut("198.51.100.7", Now.AddSeconds(59)));
        Assert.False(limiter.IsLockedOut("198.51.100.7", Now.AddSeconds(61)));
    }
}
