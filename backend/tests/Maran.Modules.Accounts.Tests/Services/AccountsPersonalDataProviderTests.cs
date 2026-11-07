using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// The account's personal-data export: it names the allow-listed fields of the named account and nobody
/// else's, and carries no field a secret or output text could ride in.
/// </summary>
public sealed class AccountsPersonalDataProviderTests
{
    /// <summary>Fragments no exported field name may contain: the vocabulary of secrets and of output text.</summary>
    private static readonly string[] ForbiddenFragments =
    [
        "password", "hash", "secret", "token", "privatekey", "output", "stdout", "stderr", "log", "sha256", "digest",
    ];

    /// <summary>The fields the account record is allowed to carry, and no others.</summary>
    private static readonly string[] AllowedFields =
    [
        "id", "name", "primaryDomain", "ownerEmail", "ownerLanguage", "status", "createdAt", "planName", "resellerId",
    ];

    /// <summary>The account export is exactly the allow-listed fields of the named account.</summary>
    [Fact]
    public async Task The_account_export_is_exactly_the_allow_listed_fields_of_the_named_account()
    {
        await using var context = AccountsTestContext.Create(FakeCurrentUser.Admin());
        var plan = new Plan(Guid.NewGuid(), "StarterPlan", 1024, 5, 5, 5, 5, 2);
        var mine = new Account(
            Guid.NewGuid(), "mine", "mine.example.com", plan.Id, DateTimeOffset.UnixEpoch, "owner@example.com");
        var theirs = new Account(
            Guid.NewGuid(), "theirs", "theirs.example.com", plan.Id, DateTimeOffset.UnixEpoch, "other@example.com");
        context.Plans.Add(plan);
        context.Accounts.AddRange(mine, theirs);
        await context.SaveChangesAsync();

        var sections = await new AccountsPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, mine.Id), CancellationToken.None);

        var section = Assert.Single(sections);
        var record = Assert.Single(section.Records);
        Assert.Equal(AllowedFields.Order(StringComparer.Ordinal), record.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("mine", record.Fields["name"]);
        Assert.Equal("owner@example.com", record.Fields["ownerEmail"]);
        Assert.Equal("StarterPlan", record.Fields["planName"]);
    }

    /// <summary>No exported field name belongs to the vocabulary of secrets.</summary>
    [Fact]
    public async Task No_exported_field_name_belongs_to_the_vocabulary_of_secrets()
    {
        await using var context = AccountsTestContext.Create(FakeCurrentUser.Admin());
        var plan = new Plan(Guid.NewGuid(), "StarterPlan", 1024, 5, 5, 5, 5, 2);
        var account = new Account(Guid.NewGuid(), "mine", "mine.example.com", plan.Id, DateTimeOffset.UnixEpoch);
        context.Plans.Add(plan);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        var sections = await new AccountsPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, account.Id), CancellationToken.None);

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
        await using var context = AccountsTestContext.Create(FakeCurrentUser.Admin());

        var sections = await new AccountsPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.User, Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(sections);
    }
}
