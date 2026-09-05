using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.Enums;

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

        Assert.Equal("hunter2", settings.Password);
        Assert.Equal("smtp.example.net", settings.Host);
        Assert.Equal(465, settings.Port);
        Assert.Equal(SmtpSecurity.ImplicitTls, settings.Security);
        Assert.Equal(Saved.AddDays(1), settings.UpdatedAt);
    }

    /// <summary>Saving an empty password clears it, which is what a relay taking no credentials needs.</summary>
    [Fact]
    public void Saving_an_empty_password_clears_it()
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

        Assert.Equal(string.Empty, settings.Password);
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
