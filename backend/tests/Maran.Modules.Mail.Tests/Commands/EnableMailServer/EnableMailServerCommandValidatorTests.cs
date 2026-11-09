using Maran.Modules.Mail.Commands.EnableMailServer;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Commands.EnableMailServer;

/// <summary>
/// Behaviour of the validator that guards the settings an administrator supplies; the rules are
/// shared with the settings update, which has its own tests of the same shape.
/// </summary>
public sealed class EnableMailServerCommandValidatorTests
{
    /// <summary>The validator under test; it holds no state, so one instance serves every case.</summary>
    private readonly EnableMailServerCommandValidator _validator = new();

    /// <summary>A well formed command is accepted, the inverse control for every refusal below.</summary>
    [Fact]
    public void A_well_formed_command_is_accepted()
    {
        var result = _validator.Validate(MailServerCommands.Enable());

        Assert.True(result.IsValid);
    }

    /// <summary>The documented extremes are accepted.</summary>
    [Fact]
    public void The_documented_extremes_are_accepted()
    {
        var result = _validator.Validate(MailServerCommands.Enable(sizeMib: 512, greylist: 0, header: 1, reject: 100));

        Assert.True(result.IsValid);
    }

    /// <summary>A host name carrying a trailing newline is refused, since it is written into line-oriented files.</summary>
    [Fact]
    public void A_host_name_carrying_a_trailing_newline_is_refused()
    {
        var result = _validator.Validate(MailServerCommands.Enable(hostname: "mail.example.com\n"));

        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == MailServerErrorCodes.MailServerHostnameInvalidFormat;
        });
    }

    /// <summary>A host name longer than a DNS name may be is refused with the too-long code.</summary>
    [Fact]
    public void A_host_name_longer_than_a_dns_name_is_refused_with_the_too_long_code()
    {
        var result = _validator.Validate(MailServerCommands.Enable(hostname: new string('a', 254)));

        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == MailServerErrorCodes.MailServerHostnameTooLong;
        });
    }

    /// <summary>A reserved host name is refused with the reserved code.</summary>
    /// <param name="hostname">The reserved name.</param>
    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("127.0.0.1")]
    [InlineData("foo.localhost")]
    [InlineData("x.internal")]
    public void A_reserved_host_name_is_refused_with_the_reserved_code(string hostname)
    {
        var result = _validator.Validate(MailServerCommands.Enable(hostname: hostname));

        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == MailServerErrorCodes.MailServerHostnameReserved;
        });
    }

    /// <summary>A name that merely resembles a reserved one is accepted.</summary>
    /// <param name="hostname">The name.</param>
    [Theory]
    [InlineData("mylocal.com")]
    [InlineData("localhost.example.com")]
    public void A_name_that_resembles_a_reserved_one_is_accepted(string hostname)
    {
        var result = _validator.Validate(MailServerCommands.Enable(hostname: hostname));

        Assert.True(result.IsValid);
    }

    /// <summary>A message size outside 1..512 MiB is refused.</summary>
    /// <param name="sizeMib">The size.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(513)]
    public void A_message_size_outside_the_range_is_refused(int sizeMib)
    {
        var result = _validator.Validate(MailServerCommands.Enable(sizeMib: sizeMib));

        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == MailServerErrorCodes.MailMessageSizeOutOfRange;
        });
    }

    /// <summary>Thresholds that are out of range or not strictly increasing are refused.</summary>
    /// <param name="greylist">The greylist threshold.</param>
    /// <param name="header">The add-header threshold.</param>
    /// <param name="reject">The reject threshold.</param>
    [Theory]
    [InlineData(-1, 6, 15)]
    [InlineData(6, 6, 15)]
    [InlineData(4, 15, 15)]
    [InlineData(10, 6, 15)]
    [InlineData(4, 6, 101)]
    [InlineData(4, 150, 160)]
    public void Spam_thresholds_out_of_range_or_not_increasing_are_refused(int greylist, int header, int reject)
    {
        var result = _validator.Validate(MailServerCommands.Enable(greylist: greylist, header: header, reject: reject));

        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == MailServerErrorCodes.MailSpamScoresInvalid;
        });
    }
}
