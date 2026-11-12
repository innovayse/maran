using System.Net;
using FluentValidation.TestHelper;
using Maran.Modules.Notifications.Commands.SaveSmtpSettings;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Options;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Notifications.Tests.Commands.SaveSmtpSettings;

/// <summary>What the panel refuses to save as mail settings, and why each refusal is there.</summary>
public sealed class SaveSmtpSettingsCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly SaveSmtpSettingsCommandValidator _validator = Validator(allowLocalRelay: false);

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

    /// <summary>Port 25 of this machine is closed to local clients, so a setting that points the panel at it is refused.</summary>
    [Theory]
    [InlineData("localhost")]
    [InlineData("LocalHost.")]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    public void Port_25_of_this_machine_is_refused(string host)
    {
        _validator.TestValidate(Valid() with { Host = host, Port = 25 })
            .ShouldHaveValidationErrorFor(candidate => candidate.Port);
    }

    /// <summary>Inverse controls: the submission port of this machine, and port 25 of another one, are accepted.</summary>
    [Theory]
    [InlineData("localhost", 587)]
    [InlineData("127.0.0.1", 465)]
    [InlineData("smtp.example.com", 25)]
    [InlineData("203.0.113.9", 25)]
    public void The_submission_port_of_this_machine_and_port_25_elsewhere_are_accepted(string host, int port)
    {
        _validator.TestValidate(Valid() with { Host = host, Port = port }).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>The machine's own public address and name are this machine too, not only loopback.</summary>
    [Theory]
    [InlineData("198.51.100.20")]
    [InlineData("2001:db8::20")]
    [InlineData("[2001:db8::20]")]
    [InlineData("::ffff:198.51.100.20")]
    [InlineData("panel.host.test")]
    [InlineData("PANEL.host.test.")]
    public void Port_25_of_the_machines_own_address_or_name_is_refused(string host)
    {
        _validator.TestValidate(Valid() with { Host = host, Port = 25 })
            .ShouldHaveValidationErrorFor(candidate => candidate.Port);
    }

    /// <summary>Inverse control: the same addresses on the submission port, and other hosts on port 25, are accepted.</summary>
    [Theory]
    [InlineData("198.51.100.20", 587)]
    [InlineData("panel.host.test", 465)]
    [InlineData("198.51.100.21", 25)]
    [InlineData("other.host.test", 25)]
    public void The_machines_own_address_on_another_port_and_other_hosts_on_port_25_are_accepted(string host, int port)
    {
        _validator.TestValidate(Valid() with { Host = host, Port = port }).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>With the deployment's explicit opt-in a relay of its own on port 25 of this machine is accepted.</summary>
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("198.51.100.20")]
    [InlineData("panel.host.test")]
    public void The_opt_in_allows_a_relay_of_its_own_on_port_25(string host)
    {
        var optedIn = Validator(allowLocalRelay: true);

        optedIn.TestValidate(Valid() with { Host = host, Port = 25 }).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>An address the machine gains after the validator exists is known to the next validation.</summary>
    [Fact]
    public void An_address_added_after_start_is_recognised_at_the_next_validation()
    {
        var machine = new ChangingMachine(new ThisMachineFacts([IPAddress.Parse("198.51.100.20")], []));
        var validator = new SaveSmtpSettingsCommandValidator(
            new OptionsWrapper<SmtpLocalRelayOptions>(new SmtpLocalRelayOptions()),
            machine);
        var command = Valid() with { Host = "203.0.113.99", Port = 25 };

        validator.TestValidate(command).ShouldNotHaveAnyValidationErrors();

        machine.Facts = new ThisMachineFacts([IPAddress.Parse("198.51.100.20"), IPAddress.Parse("203.0.113.99")], []);

        validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.Port);
    }

    /// <summary>Builds the validator for a machine with one public address and one name.</summary>
    /// <param name="allowLocalRelay">The deployment's opt-in.</param>
    /// <returns>The validator.</returns>
    private static SaveSmtpSettingsCommandValidator Validator(bool allowLocalRelay)
    {
        return new SaveSmtpSettingsCommandValidator(
            new OptionsWrapper<SmtpLocalRelayOptions>(
                new SmtpLocalRelayOptions { AllowLocalPort25Relay = allowLocalRelay }),
            new ChangingMachine(
                new ThisMachineFacts([IPAddress.Parse("198.51.100.20"), IPAddress.Parse("2001:db8::20")], ["panel.host.test"])));
    }

    /// <summary>A machine whose facts a test can change between two validations.</summary>
    /// <param name="initial">The facts the first read returns.</param>
    private sealed class ChangingMachine(ThisMachineFacts initial) : IThisMachineFactsSource
    {
        /// <summary>The facts the next read returns.</summary>
        public ThisMachineFacts Facts { get; set; } = initial;

        /// <inheritdoc />
        public ThisMachineFacts Read()
        {
            return Facts;
        }
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
