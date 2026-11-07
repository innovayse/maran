using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>
/// The SSL personal-data export: the certificates of the named account, nobody else's, and no field a
/// secret could ride in.
/// </summary>
public sealed class SslPersonalDataProviderTests
{
    /// <summary>Fragments no exported field name may contain: the vocabulary of secrets and of output text.</summary>
    private static readonly string[] ForbiddenFragments =
    [
        "password", "hash", "secret", "token", "privatekey", "output", "stdout", "stderr", "log", "sha256", "digest",
    ];

    /// <summary>The fields a certificate record may carry, and no others.</summary>
    private static readonly string[] AllowedFields = ["id", "siteId", "domain", "issuer", "issuedAt", "notAfter"];

    /// <summary>The export holds exactly the allow-listed fields of the named account's certificates.</summary>
    [Fact]
    public async Task The_export_holds_exactly_the_allow_listed_fields_of_the_named_accounts_certificates()
    {
        var mine = Guid.NewGuid();
        var expiry = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using var context = SslTestContext.Create(FakeCurrentUser.Admin());
        context.Certificates.Add(SslTestContext.Certificate(mine, "one.example.com", expiry));
        context.Certificates.Add(SslTestContext.Certificate(Guid.NewGuid(), "theirs.example.com", expiry));
        await context.SaveChangesAsync();

        var sections = await new SslPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, mine), CancellationToken.None);

        var section = Assert.Single(sections);
        var record = Assert.Single(section.Records);
        Assert.Equal(AllowedFields.Order(StringComparer.Ordinal), record.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("one.example.com", record.Fields["domain"]);

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
        await using var context = SslTestContext.Create(FakeCurrentUser.Admin());

        var sections = await new SslPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.User, Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(sections);
    }
}
