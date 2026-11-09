using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>Writes the rows an alias test starts from, straight into the database and past every handler.</summary>
public static class MailAliasSeeds
{
    /// <summary>Turns mail on with the external-forwarding switch set as asked.</summary>
    /// <param name="context">The assembled module.</param>
    /// <param name="allowExternalForwarding">Whether forwarders may leave the account's domains.</param>
    /// <returns>The settings row.</returns>
    public static async Task<MailServerSettings> SeedServerAsync(MailTestContext context, bool allowExternalForwarding)
    {
        var settings = new MailServerSettings(
            "mail.example.com",
            pop3Enabled: false,
            antivirusEnabled: false,
            allowExternalForwarding,
            MailDefaults.MessageSizeMib,
            MailDefaults.SpamGreylistScore,
            MailDefaults.SpamAddHeaderScore,
            MailDefaults.SpamRejectScore,
            context.Clock.UtcNow);
        context.Database.MailServerSettings.Add(settings);
        await context.Database.SaveChangesAsync();

        return settings;
    }

    /// <summary>Turns mail on and then off again, as an administrator who disabled the stack.</summary>
    /// <param name="context">The assembled module.</param>
    public static async Task SeedDisabledServerAsync(MailTestContext context)
    {
        var settings = await SeedServerAsync(context, allowExternalForwarding: true);
        settings.Disable(context.Clock.UtcNow);
        await context.Database.SaveChangesAsync();
    }

    /// <summary>Writes one address with its destinations.</summary>
    /// <param name="context">The assembled module.</param>
    /// <param name="domain">The domain it lives in.</param>
    /// <param name="localPart">The local part, or the catch-all marker.</param>
    /// <param name="kind">What the address is.</param>
    /// <param name="targets">Its destinations, in order.</param>
    /// <returns>The row.</returns>
    public static async Task<MailAlias> SeedAliasAsync(
        MailTestContext context,
        MailDomain domain,
        string localPart,
        MailAliasKind kind,
        params string[] targets)
    {
        return await SeedAliasAsync(context, domain, localPart, kind, context.Clock.UtcNow, targets);
    }

    /// <summary>Writes one address with its destinations, created at a given instant.</summary>
    /// <param name="context">The assembled module.</param>
    /// <param name="domain">The domain it lives in.</param>
    /// <param name="localPart">The local part, or the catch-all marker.</param>
    /// <param name="kind">What the address is.</param>
    /// <param name="createdAt">The instant it was created.</param>
    /// <param name="targets">Its destinations, in order.</param>
    /// <returns>The row.</returns>
    public static async Task<MailAlias> SeedAliasAsync(
        MailTestContext context,
        MailDomain domain,
        string localPart,
        MailAliasKind kind,
        DateTimeOffset createdAt,
        params string[] targets)
    {
        var alias = new MailAlias(Guid.NewGuid(), domain.AccountId, domain.Id, localPart, kind, targets, createdAt);
        context.Database.MailAliases.Add(alias);
        await context.Database.SaveChangesAsync();

        return alias;
    }
}
