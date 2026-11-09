using Maran.Modules.Mail.Commands.UpdateMailbox;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Commands.UpdateMailbox;

/// <summary>Behavioral contract of <see cref="UpdateMailboxCommandValidator"/>.</summary>
public sealed class UpdateMailboxCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly UpdateMailboxCommandValidator _validator = new();

    /// <summary>The ends of both ranges are accepted — the inverse control for the refusals.</summary>
    [Fact]
    public void The_ends_of_both_ranges_are_accepted()
    {
        Assert.True(_validator.Validate(new UpdateMailboxCommand(1, 1, true)).IsValid);
        Assert.True(_validator.Validate(
            new UpdateMailboxCommand(MailDefaults.MaxMailboxQuotaMib, MailDefaults.MaxSendLimitPerHour, false)).IsValid);
    }

    /// <summary>A quota outside 1 to the ceiling is refused with its own code.</summary>
    /// <param name="quota">The quota sent.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MailDefaults.MaxMailboxQuotaMib + 1)]
    public void A_quota_outside_the_range_is_refused(int quota)
    {
        var result = _validator.Validate(new UpdateMailboxCommand(quota, 10, true));

        Assert.Equal(MailboxGroupErrorCodes.MailboxQuotaOutOfRange, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A send limit outside 1 to the ceiling is refused with its own code.</summary>
    /// <param name="limit">The send limit sent.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MailDefaults.MaxSendLimitPerHour + 1)]
    public void A_send_limit_outside_the_range_is_refused(int limit)
    {
        var result = _validator.Validate(new UpdateMailboxCommand(1024, limit, true));

        Assert.Equal(MailboxGroupErrorCodes.MailboxSendLimitOutOfRange, Assert.Single(result.Errors).ErrorMessage);
    }
}
