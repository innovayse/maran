using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Ftp.Services;
using Maran.Modules.Ftp.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ftp.Tests.Services;

/// <summary>
/// The FTP personal-data export: the login names of the named account, nobody else's, and no field a
/// secret could ride in.
/// </summary>
public sealed class FtpPersonalDataProviderTests
{
    /// <summary>Fragments no exported field name may contain: the vocabulary of secrets and of output text.</summary>
    private static readonly string[] ForbiddenFragments =
    [
        "password", "hash", "secret", "token", "privatekey", "output", "stdout", "stderr", "log", "sha256", "digest",
    ];

    /// <summary>The fields an FTP login record may carry, and no others.</summary>
    private static readonly string[] AllowedFields = ["id", "userName", "createdAt"];

    /// <summary>The export holds exactly the allow-listed fields of the named account's FTP logins.</summary>
    [Fact]
    public async Task The_export_holds_exactly_the_allow_listed_fields_of_the_named_accounts_logins()
    {
        var mine = Guid.NewGuid();
        await using var context = Open();
        context.FtpUsers.Add(new FtpUser(Guid.NewGuid(), mine, "web", "acct_web", DateTimeOffset.UnixEpoch));
        context.FtpUsers.Add(new FtpUser(Guid.NewGuid(), Guid.NewGuid(), "web", "other_web", DateTimeOffset.UnixEpoch));
        await context.SaveChangesAsync();

        var sections = await new FtpPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, mine), CancellationToken.None);

        var section = Assert.Single(sections);
        var record = Assert.Single(section.Records);
        Assert.Equal(AllowedFields.Order(StringComparer.Ordinal), record.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("acct_web", record.Fields["userName"]);

        var names = sections.SelectMany(section => { return section.Records; })
            .SelectMany(record => { return record.Fields.Keys; })
            .ToList();
        Assert.NotEmpty(names);
        Assert.All(names, name =>
        {
            Assert.DoesNotContain(
                ForbiddenFragments,
                fragment => { return name.Contains(fragment, StringComparison.OrdinalIgnoreCase); });
        });
    }

    /// <summary>A user subject gets an empty list, because this module holds no row about a login.</summary>
    [Fact]
    public async Task A_user_subject_gets_an_empty_list()
    {
        await using var context = Open();

        var sections = await new FtpPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.User, Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(sections);
    }

    /// <summary>Opens a context over a private in-memory database, seen as an administrator.</summary>
    /// <returns>The context.</returns>
    private static FtpDbContext Open()
    {
        var options = new DbContextOptionsBuilder<FtpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FtpDbContext(options, new TestCurrentUser());
    }
}
