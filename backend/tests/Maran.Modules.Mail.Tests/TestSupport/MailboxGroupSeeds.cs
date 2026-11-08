using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Npgsql;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>Rows and failures the mailbox tests share, so each test names only what it is about.</summary>
public static class MailboxGroupSeeds
{
    /// <summary>The password every test in this group sets; it must appear nowhere but in the command.</summary>
    public const string Password = "correct horse battery";

    /// <summary>Adds an alias with one target to a domain, as the address-space clash test needs.</summary>
    /// <param name="context">The test context whose database receives the alias.</param>
    /// <param name="domain">The domain the alias lives in.</param>
    /// <param name="localPart">The alias's local part.</param>
    /// <returns>The recorded alias.</returns>
    public static async Task<MailAlias> SeedAliasAsync(MailTestContext context, MailDomain domain, string localPart)
    {
        var alias = new MailAlias(
            Guid.NewGuid(),
            domain.AccountId,
            domain.Id,
            localPart,
            MailAliasKind.Alias,
            ["target@" + domain.Name],
            context.Clock.UtcNow);
        context.Database.MailAliases.Add(alias);
        await context.Database.SaveChangesAsync();

        return alias;
    }

    /// <summary>Builds the failure PostgreSQL raises for a duplicate key.</summary>
    /// <returns>A <see cref="PostgresException"/> carrying SqlState <c>23505</c>.</returns>
    public static PostgresException UniqueViolation()
    {
        return new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation);
    }
}
