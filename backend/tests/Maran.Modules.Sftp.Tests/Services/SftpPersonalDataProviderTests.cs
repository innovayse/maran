using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Services;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sftp.Tests.Services;

/// <summary>
/// The SFTP personal-data export: the logins of the named account and the label, algorithm and
/// fingerprint of their keys, nobody else's, and neither the key text nor any credential.
/// </summary>
public sealed class SftpPersonalDataProviderTests
{
    /// <summary>Fragments no exported field name may contain: the vocabulary of secrets and of output text.</summary>
    private static readonly string[] ForbiddenFragments =
    [
        "password", "hash", "secret", "token", "privatekey", "output", "stdout", "stderr", "log", "sha256", "digest",
    ];

    /// <summary>The fields an SFTP login record may carry, and no others.</summary>
    private static readonly string[] UserFields = ["id", "userName", "createdAt"];

    /// <summary>The fields an SFTP key record may carry, and no others.</summary>
    private static readonly string[] KeyFields = ["id", "userId", "label", "algorithm", "fingerprint", "createdAt"];

    /// <summary>The export holds exactly the allow-listed fields of the named account's logins and keys.</summary>
    [Fact]
    public async Task The_export_holds_exactly_the_allow_listed_fields_of_the_named_accounts_logins_and_keys()
    {
        var mine = Guid.NewGuid();
        await using var context = SftpTestContext.Create(FakeCurrentUser.Admin());
        var login = SftpTestContext.Row(mine, "acct", "deploy");
        var keyLine = SftpTestKeys.Ed25519Filled(7);
        login.AddKey(Guid.NewGuid(), "laptop", SftpPublicKey.Parse(keyLine).Value, DateTimeOffset.UnixEpoch);
        context.SftpUsers.Add(login);
        context.SftpUsers.Add(SftpTestContext.Row(Guid.NewGuid(), "other", "deploy"));
        await context.SaveChangesAsync();

        var sections = await new SftpPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, mine), CancellationToken.None);

        var users = Assert.Single(sections, section => { return section.Name == "users"; });
        var user = Assert.Single(users.Records);
        Assert.Equal(UserFields.Order(StringComparer.Ordinal), user.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("acct_deploy", user.Fields["userName"]);

        var keys = Assert.Single(sections, section => { return section.Name == "keys"; });
        var key = Assert.Single(keys.Records);
        Assert.Equal(KeyFields.Order(StringComparer.Ordinal), key.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("laptop", key.Fields["label"]);

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

    /// <summary>The public key text is stored on the row and is not in the export.</summary>
    /// <remarks>
    /// The positive control is the first assertion: the stored key row does hold the key text, so the
    /// absence asserted second is a property of the provider and not of an empty row.
    /// </remarks>
    [Fact]
    public async Task The_public_key_text_is_not_exported()
    {
        var mine = Guid.NewGuid();
        await using var context = SftpTestContext.Create(FakeCurrentUser.Admin());
        var login = SftpTestContext.Row(mine, "acct", "deploy");
        var keyLine = SftpTestKeys.Ed25519Filled(9);
        login.AddKey(Guid.NewGuid(), "laptop", SftpPublicKey.Parse(keyLine).Value, DateTimeOffset.UnixEpoch);
        context.SftpUsers.Add(login);
        await context.SaveChangesAsync();
        var stored = context.SftpUserKeys.Single().PublicKey;
        Assert.False(string.IsNullOrEmpty(stored));

        var sections = await new SftpPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, mine), CancellationToken.None);

        var values = sections.SelectMany(section => { return section.Records; })
            .SelectMany(record => { return record.Fields.Values; })
            .ToList();
        Assert.NotEmpty(values);
        Assert.DoesNotContain(
            values,
            value => { return value is not null && value.Contains(stored, StringComparison.Ordinal); });
    }

    /// <summary>A user subject gets an empty list, because this module holds no row about a login.</summary>
    [Fact]
    public async Task A_user_subject_gets_an_empty_list()
    {
        await using var context = SftpTestContext.Create(FakeCurrentUser.Admin());

        var sections = await new SftpPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.User, Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(sections);
    }
}
