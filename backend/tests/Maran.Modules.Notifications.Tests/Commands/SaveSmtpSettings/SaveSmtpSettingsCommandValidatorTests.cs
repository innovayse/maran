using FluentValidation.TestHelper;
using Maran.Modules.Notifications.Commands.SaveSmtpSettings;
using Maran.Modules.Notifications.Domain.Enums;

namespace Maran.Modules.Notifications.Tests.Commands.SaveSmtpSettings;

/// <summary>What the panel refuses to save as mail settings, and why each refusal is there.</summary>
public sealed class SaveSmtpSettingsCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly SaveSmtpSettingsCommandValidator _validator = new();

    /// <summary>A well-formed configuration is accepted.</summary>
    [Fact]
    public void A_well_formed_configuration_is_accepted()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A newline in the sender's display name is refused, because it would invent a mail header.</summary>
    /// <remarks>
    /// The panel's equivalent of SQL injection for a line-oriented format: one embedded newline turns
    /// one header into two, which is how a <c>Bcc:</c> nobody wrote reaches the message
    /// (rules/security.md item 4).
    /// </remarks>
    [Fact]
    public void A_newline_in_the_sender_name_is_refused_because_it_would_invent_a_mail_header()
    {
        var command = Valid() with { FromName = "Panel\r\nBcc: attacker@example.net" };

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(candidate => candidate.FromName);
    }

    /// <summary>A newline in the mail server's name is refused for the same reason.</summary>
    [Fact]
    public void A_newline_in_the_host_is_refused()
    {
        var command = Valid() with { Host = "smtp.example.com\r\nEVIL" };

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(candidate => candidate.Host);
    }

    /// <summary>A missing mail server is refused: the panel cannot send through nothing.</summary>
    [Fact]
    public void An_empty_host_is_refused()
    {
        _validator.TestValidate(Valid() with { Host = string.Empty })
            .ShouldHaveValidationErrorFor(candidate => candidate.Host);
    }

    /// <summary>A port outside the TCP range is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65_536)]
    public void A_port_outside_the_tcp_range_is_refused(int port)
    {
        _validator.TestValidate(Valid() with { Port = port }).ShouldHaveValidationErrorFor(candidate => candidate.Port);
    }

    /// <summary>A security mode outside the offered set is refused rather than silently downgraded.</summary>
    /// <remarks>
    /// A JSON body binds to an enum by NUMBER as readily as by name, so <c>"security": 99</c> produces
    /// a perfectly typed value that matches no member. Refusing it here is what stops it reaching the
    /// mailer's default arm.
    /// </remarks>
    [Fact]
    public void A_security_mode_outside_the_offered_set_is_refused()
    {
        var command = Valid() with { Security = (SmtpSecurity)99 };

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(candidate => candidate.Security);
    }

    /// <summary>
    /// A request that never mentions the security mode is refused, and cleartext has to be named
    /// (issue #491).
    /// </summary>
    /// <remarks>
    /// The omitted field deserializes to <c>default(SmtpSecurity)</c>, which is 0. That used to be the
    /// cleartext mode, so omitting the setting sent the submission credential unprotected; the enum has
    /// no member with the value 0 now, so the omission is an invalid mode. The accepting half is in
    /// <see cref="Every_named_security_mode_including_cleartext_is_accepted"/>.
    /// </remarks>
    [Fact]
    public void An_omitted_security_mode_is_refused_and_does_not_select_cleartext()
    {
        var command = Valid() with { Security = default };

        Assert.Equal(0, (int)command.Security);
        _validator.TestValidate(command).ShouldHaveValidationErrorFor(candidate => candidate.Security);
    }

    /// <summary>Every named mode is accepted, cleartext included, because naming it is the deliberate choice.</summary>
    /// <param name="security">A mode the picker offers.</param>
    [Theory]
    [InlineData(SmtpSecurity.StartTls)]
    [InlineData(SmtpSecurity.ImplicitTls)]
    [InlineData(SmtpSecurity.None)]
    public void Every_named_security_mode_including_cleartext_is_accepted(SmtpSecurity security)
    {
        _validator.TestValidate(Valid() with { Security = security })
            .ShouldNotHaveValidationErrorFor(candidate => candidate.Security);
    }

    /// <summary>The numbers the modes are stored as are pinned, because rows hold them.</summary>
    [Fact]
    public void The_stored_numbers_of_the_security_modes_are_pinned_and_zero_is_none_of_them()
    {
        Assert.Equal(1, (int)SmtpSecurity.StartTls);
        Assert.Equal(2, (int)SmtpSecurity.ImplicitTls);
        Assert.Equal(3, (int)SmtpSecurity.None);
        Assert.False(Enum.IsDefined((SmtpSecurity)0));
    }

    /// <summary>A sender that is not one bare address is refused.</summary>
    [Theory]
    [InlineData("Panel <panel@example.com>")]
    [InlineData("not-an-address")]
    [InlineData("")]
    public void A_sender_that_is_not_one_bare_address_is_refused(string address)
    {
        _validator.TestValidate(Valid() with { FromAddress = address })
            .ShouldHaveValidationErrorFor(candidate => candidate.FromAddress);
    }

    /// <summary>An absent password is accepted, because it means "keep the stored one".</summary>
    [Fact]
    public void An_absent_password_is_accepted_because_it_means_keep_the_stored_one()
    {
        _validator.TestValidate(Valid() with { Password = null }).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>An empty password is accepted: a relay that takes no credentials is a real configuration.</summary>
    [Fact]
    public void An_empty_password_is_accepted_because_a_relay_may_take_none()
    {
        _validator.TestValidate(Valid() with { Password = string.Empty }).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A configuration the panel would accept.</summary>
    /// <returns>The command.</returns>
    private static SaveSmtpSettingsCommand Valid()
    {
        return new SaveSmtpSettingsCommand(
            "smtp.example.com",
            587,
            SmtpSecurity.StartTls,
            "panel",
            "hunter2",
            "panel@example.com",
            "Maran Panel",
            "203.0.113.7",
            "curl/8");
    }
}
