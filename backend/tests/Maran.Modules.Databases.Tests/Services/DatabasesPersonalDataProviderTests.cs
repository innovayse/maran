using Maran.Modules.Databases.Services;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Databases.Tests.Services;

/// <summary>
/// The databases personal-data export: the names of the named account's databases and database users,
/// nobody else's, and no field a secret could ride in.
/// </summary>
public sealed class DatabasesPersonalDataProviderTests
{
    /// <summary>Fragments no exported field name may contain: the vocabulary of secrets and of output text.</summary>
    private static readonly string[] ForbiddenFragments =
    [
        "password", "hash", "secret", "token", "privatekey", "output", "stdout", "stderr", "log", "sha256", "digest",
    ];

    /// <summary>The fields a database record may carry, and no others.</summary>
    private static readonly string[] AllowedFields = ["id", "databaseName", "databaseUserName", "createdAt"];

    /// <summary>The export holds exactly the allow-listed fields of the named account's databases.</summary>
    [Fact]
    public async Task The_export_holds_exactly_the_allow_listed_fields_of_the_named_accounts_databases()
    {
        var mine = Guid.NewGuid();
        await using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin());
        context.Databases.Add(DatabasesTestContext.Row(mine, "acct", "shop"));
        context.Databases.Add(DatabasesTestContext.Row(Guid.NewGuid(), "other", "blog"));
        await context.SaveChangesAsync();

        var sections = await new DatabasesPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, mine), CancellationToken.None);

        var section = Assert.Single(sections);
        var record = Assert.Single(section.Records);
        Assert.Equal(AllowedFields.Order(StringComparer.Ordinal), record.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("acct_shop", record.Fields["databaseName"]);

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
        await using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin());

        var sections = await new DatabasesPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.User, Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(sections);
    }
}
