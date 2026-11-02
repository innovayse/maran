using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Domain.ValueObjects;

namespace Maran.Modules.Notifications.Tests.Domain;

/// <summary>The one-row mail settings, and the rule that keeps a save from silently unauthenticating them.</summary>
public sealed class SmtpSettingsTests
{
    /// <summary>The instant the fixture's settings were first saved.</summary>
    private static readonly DateTimeOffset Saved = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Saving without a new password keeps the stored one.</summary>
    /// <remarks>
    /// The settings form cannot show the stored password — nothing ever returns it — so it submits no
    /// password when the administrator did not retype one. A save that read that as "clear it" would
    /// unauthenticate the panel's mail the first time anybody changed the port.
    /// </remarks>
    [Fact]
    public void Saving_without_a_new_password_keeps_the_stored_one()
    {
        var settings = Existing();

        settings.Replace(
            "smtp.example.net",
            465,
            SmtpSecurity.ImplicitTls,
            "postmaster",
            password: null,
            "panel@example.net",
            "Panel",
            "ops@example.net",
            Saved.AddDays(1));

        Assert.Equal("hunter2", settings.BasicPassword);
        Assert.Equal("smtp.example.net", settings.Host);
        Assert.Equal(465, settings.Port);
        Assert.Equal(SmtpSecurity.ImplicitTls, settings.Security);
        Assert.Equal(Saved.AddDays(1), settings.UpdatedAt);
    }

    /// <summary>Saving a blank user name moves the settings to anonymous mode and leaves no credentials behind.</summary>
    [Fact]
    public void Saving_a_blank_user_name_clears_the_credentials()
    {
        var settings = Existing();

        settings.Replace(
            "127.0.0.1",
            25,
            SmtpSecurity.None,
            string.Empty,
            password: string.Empty,
            "panel@example.net",
            "Panel",
            "ops@example.net",
            Saved.AddDays(1));

        Assert.Equal(SmtpAuthentication.Anonymous, settings.AuthenticationMode);
        Assert.Null(settings.BasicUsername);
        Assert.Null(settings.BasicPassword);
    }

    /// <summary>A stored user name reads as credentials carrying it and the stored password.</summary>
    [Fact]
    public void A_stored_user_name_reads_as_basic_credentials()
    {
        var credentials = Assert.IsType<BasicSmtpCredentials>(Existing().Credentials);

        Assert.Equal("panel", credentials.Username);
        Assert.Equal("hunter2", credentials.Password);
    }

    /// <summary>An empty stored user name reads as anonymous credentials, whatever else is stored.</summary>
    [Fact]
    public void An_empty_user_name_reads_as_anonymous_credentials()
    {
        var settings = Existing();
        settings.Replace(
            "127.0.0.1",
            25,
            SmtpSecurity.None,
            string.Empty,
            password: string.Empty,
            "panel@example.net",
            "Panel",
            "ops@example.net",
            Saved.AddDays(1));

        Assert.IsType<AnonymousSmtpCredentials>(settings.Credentials);
    }

    /// <summary>Settings created with a blank user name are anonymous and hold no credentials at all.</summary>
    [Fact]
    public void Settings_created_with_a_blank_user_name_are_anonymous_with_no_credentials()
    {
        var settings = new SmtpSettings(
            "127.0.0.1",
            25,
            SmtpSecurity.None,
            string.Empty,
            string.Empty,
            "panel@example.com",
            "Panel",
            string.Empty,
            Saved);

        Assert.Equal(SmtpAuthentication.Anonymous, settings.AuthenticationMode);
        Assert.Null(settings.BasicUsername);
        Assert.Null(settings.BasicPassword);
        Assert.Null(settings.AlertRecipientAddress);
        Assert.IsType<AnonymousSmtpCredentials>(settings.Credentials);
    }

    /// <summary>Moving from anonymous to basic without typing a password stores an empty one, not a stale one.</summary>
    [Fact]
    public void Moving_from_anonymous_to_basic_without_a_password_stores_an_empty_one()
    {
        var settings = Existing();
        settings.Replace("127.0.0.1", 25, SmtpSecurity.None, string.Empty, null, "panel@example.com", "Panel", "ops@example.com", Saved);

        settings.Replace("smtp.example.com", 587, SmtpSecurity.StartTls, "panel", null, "panel@example.com", "Panel", "ops@example.com", Saved);

        var credentials = Assert.IsType<BasicSmtpCredentials>(settings.Credentials);
        Assert.Equal("panel", credentials.Username);
        Assert.Equal(string.Empty, credentials.Password);
    }

    /// <summary>The basic credentials never print the password when formatted.</summary>
    [Fact]
    public void Basic_credentials_never_print_the_password_when_formatted()
    {
        var text = Existing().Credentials.ToString();

        Assert.Contains("panel", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
    }

    /// <summary>A blank stored alert recipient reads as no address, and a set one as itself.</summary>
    [Fact]
    public void A_blank_alert_recipient_reads_as_no_address()
    {
        var settings = Existing();
        Assert.Equal("ops@example.com", settings.AlertRecipientAddress);

        settings.Replace(
            "smtp.example.com",
            587,
            SmtpSecurity.StartTls,
            "panel",
            password: null,
            "panel@example.com",
            "Panel",
            "  ",
            Saved.AddDays(1));

        Assert.Null(settings.AlertRecipientAddress);
    }

    /// <summary>The row's key is the fixed singleton id, which is what makes a second row impossible.</summary>
    [Fact]
    public void The_row_always_carries_the_singleton_id()
    {
        Assert.Equal(SmtpSettings.SingletonId, Existing().Id);
    }

    /// <summary>Settings as a panel that has already configured mail holds them.</summary>
    /// <returns>The settings.</returns>
    private static SmtpSettings Existing()
    {
        return new SmtpSettings(
            "smtp.example.com",
            587,
            SmtpSecurity.StartTls,
            "panel",
            "hunter2",
            "panel@example.com",
            "Panel",
            "ops@example.com",
            Saved);
    }
}
