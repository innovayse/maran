using Maran.Modules.Mail.Commands.UpdateMailServerSettings;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Commands.UpdateMailServerSettings;

/// <summary>Behaviour of the settings update's validator, which shares its rules with the enable command's.</summary>
public sealed class UpdateMailServerSettingsCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly UpdateMailServerSettingsCommandValidator _validator = new();

    /// <summary>A well formed update is accepted.</summary>
    [Fact]
    public void A_well_formed_update_is_accepted()
    {
        Assert.True(_validator.Validate(MailServerCommands.Update()).IsValid);
    }

    /// <summary>A reserved host name is refused, so the shared host name rules apply to an update too.</summary>
    [Fact]
    public void A_reserved_host_name_is_refused()
    {
        var result = _validator.Validate(MailServerCommands.Update(hostname: "127.0.0.1"));

        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == MailServerErrorCodes.MailServerHostnameReserved;
        });
    }

    /// <summary>An oversized message limit is refused.</summary>
    [Fact]
    public void An_oversized_message_limit_is_refused()
    {
        var result = _validator.Validate(MailServerCommands.Update(sizeMib: 513));

        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == MailServerErrorCodes.MailMessageSizeOutOfRange;
        });
    }

    /// <summary>Thresholds that do not rise in order are refused.</summary>
    [Fact]
    public void Thresholds_that_do_not_rise_in_order_are_refused()
    {
        var result = _validator.Validate(MailServerCommands.Update(greylist: 9, header: 6, reject: 15));

        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == MailServerErrorCodes.MailSpamScoresInvalid;
        });
    }
}
