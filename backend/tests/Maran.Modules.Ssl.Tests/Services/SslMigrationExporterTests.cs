using System.Text.Json;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>
/// What the Ssl section of an account move holds, what the dry run tells the administrator about it, and that no
/// secret planted in a certificate row ever reaches it (issue #127, decision D2).
/// </summary>
public sealed class SslMigrationExporterTests
{
    /// <summary>The prefix of every value planted in a column the section does not name.</summary>
    private const string Planted = "PLANTED-SECRET-";

    /// <summary>The moment the certificates below end.</summary>
    private static readonly DateTimeOffset NotAfter = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Plants a recognisable value in every string column the section does not carry.</summary>
    /// <param name="certificate">The certificate row.</param>
    /// <returns>The planted values, by property name.</returns>
    private static Dictionary<string, string> PlantInEveryOtherStringColumn(Certificate certificate)
    {
        var planted = new Dictionary<string, string>();

        foreach (var property in typeof(Certificate).GetProperties().Where(p => { return p.PropertyType == typeof(string) && p.Name != "Domain"; }))
        {
            planted[property.Name] = Planted + property.Name;
            property.SetValue(certificate, planted[property.Name]);
        }

        return planted;
    }

    /// <summary>A value planted in every column the section does not name never appears in it; the carried fields do.</summary>
    [Fact]
    public async Task A_secret_planted_in_a_column_the_section_does_not_name_never_appears_in_it()
    {
        var account = Guid.NewGuid();
        await using var context = SslTestContext.Create(new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: true));
        var certificate = SslTestContext.Certificate(account, "shop.example", NotAfter, CertificateSource.Custom);
        var planted = PlantInEveryOtherStringColumn(certificate);
        context.Certificates.Add(certificate);
        await context.SaveChangesAsync();

        var section = await new SslMigrationExporter(context).ExportAsync(account, CancellationToken.None);

        Assert.NotEmpty(planted);
        Assert.NotNull(section);
        Assert.Contains("shop.example", section.Json, StringComparison.Ordinal);
        Assert.Contains("Custom", section.Json, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, section.Json, StringComparison.Ordinal);
    }

    /// <summary>The positive control: serialising the row itself DOES carry the planted values.</summary>
    [Fact]
    public void The_probe_can_see_a_planted_value_in_a_serialised_row()
    {
        var certificate = SslTestContext.Certificate(Guid.NewGuid(), "shop.example", NotAfter);
        var planted = PlantInEveryOtherStringColumn(certificate);

        var json = JsonSerializer.Serialize(certificate, JsonSerializerOptions.Web);

        Assert.NotEmpty(planted);
        Assert.All(planted.Values, value => { Assert.Contains(value, json, StringComparison.Ordinal); });
    }

    /// <summary>An account with no certificate has no section, and another account's certificate is not in it.</summary>
    [Fact]
    public async Task Only_the_named_accounts_certificates_are_exported_and_none_means_no_section()
    {
        var mine = Guid.NewGuid();
        await using var context = SslTestContext.Create(new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: true));
        context.Certificates.Add(SslTestContext.Certificate(Guid.NewGuid(), "theirs.example", NotAfter));
        await context.SaveChangesAsync();
        var exporter = new SslMigrationExporter(context);

        Assert.Null(await exporter.ExportAsync(mine, CancellationToken.None));

        context.Certificates.Add(SslTestContext.Certificate(mine, "mine.example", NotAfter));
        await context.SaveChangesAsync();
        var section = await exporter.ExportAsync(mine, CancellationToken.None);

        Assert.Contains("mine.example", section!.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("theirs.example", section.Json, StringComparison.Ordinal);
    }

    /// <summary>The dry run tells the administrator to issue an ACME certificate again and to upload a custom one again, and imports nothing.</summary>
    [Fact]
    public async Task The_dry_run_says_what_to_issue_and_what_to_upload_and_the_import_creates_nothing()
    {
        var account = Guid.NewGuid();
        await using var context = SslTestContext.Create(new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: true));
        context.Certificates.Add(SslTestContext.Certificate(account, "acme.example", NotAfter, CertificateSource.Acme));
        context.Certificates.Add(SslTestContext.Certificate(account, "custom.example", NotAfter, CertificateSource.Custom));
        await context.SaveChangesAsync();
        var section = (await new SslMigrationExporter(context).ExportAsync(account, CancellationToken.None))!;
        var importer = new SslMigrationImporter();

        var findings = await importer.PlanAsync(
            section, new MigrationPlanContext("alice", Guid.NewGuid(), new Dictionary<string, int>(), 0), CancellationToken.None);
        var imported = await importer.ImportAsync(
            section, new MigrationImportTarget(Guid.NewGuid(), "alice", new MigrationImportChoices(Guid.NewGuid(), null)), CancellationToken.None);

        Assert.Equal(
            [("acme.example", MigrationCodes.CertificateReissue), ("custom.example", MigrationCodes.CertificateUpload)],
            findings.OrderBy(f => { return f.Subject; }, StringComparer.Ordinal).Select(f => { return (f.Subject, f.Code); }));
        Assert.All(findings, finding => { Assert.Equal(MigrationFindingSeverity.Info, finding.Severity); });
        Assert.True(imported.IsSuccess);
        Assert.Empty(imported.Value!.Secrets);
    }

    /// <summary>A section of another version or module is a conflict at the dry run, and a failure at the import.</summary>
    [Fact]
    public async Task A_section_this_build_cannot_read_is_a_conflict_and_a_failure()
    {
        var importer = new SslMigrationImporter();
        var foreign = new MigrationSection("ssl", 99, "{}");

        var findings = await importer.PlanAsync(
            foreign, new MigrationPlanContext("alice", Guid.NewGuid(), new Dictionary<string, int>(), 0), CancellationToken.None);
        var imported = await importer.ImportAsync(
            foreign, new MigrationImportTarget(Guid.NewGuid(), "alice", new MigrationImportChoices(Guid.NewGuid(), null)), CancellationToken.None);

        Assert.Equal(MigrationFindingSeverity.Conflict, Assert.Single(findings).Severity);
        Assert.False(imported.IsSuccess);
    }
}
