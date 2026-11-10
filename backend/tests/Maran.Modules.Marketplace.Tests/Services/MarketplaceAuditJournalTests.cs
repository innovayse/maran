using System.Globalization;
using System.Resources;
using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Services;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>
/// Audit entries carry the module, version, digest and a reason code, and nothing a lease, a grant, a
/// download address or an exception message could ride in on.
/// </summary>
public sealed class MarketplaceAuditJournalTests
{
    /// <summary>A package digest.</summary>
    private static readonly string Digest = new('c', 64);

    /// <summary>What the journal wrote.</summary>
    private readonly RecordingAuditWriter _writer = new();

    /// <summary>The journal under test, acting as an administrator.</summary>
    private readonly MarketplaceAuditJournal _journal;

    /// <summary>Builds the journal over a recording writer.</summary>
    public MarketplaceAuditJournalTests()
    {
        _journal = new MarketplaceAuditJournal(_writer, new AdminCurrentUser());
    }

    /// <summary>The single entry the journal wrote.</summary>
    /// <returns>The entry.</returns>
    private AuditEntry Written()
    {
        return Assert.Single(_writer.Entries);
    }

    /// <summary>An accepted install request is recorded under the administrator with module, version and digest.</summary>
    [Fact]
    public async Task An_accepted_install_request_carries_the_module_version_and_digest()
    {
        await _journal.RecordRequestAsync(
            MarketplaceOperation.Install,
            new ModulePackageReference("mail", "1.2.0", Digest),
            failure: null,
            "203.0.113.5",
            "test-agent",
            CancellationToken.None);

        var entry = Written();
        Assert.Equal(MarketplaceClientAuditActions.ModuleInstallRequested, entry.Action);
        Assert.Equal($"module=mail;version=1.2.0;digest={Digest}", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal(AdminCurrentUser.UserId, entry.ActorUserId);
        Assert.Equal("203.0.113.5", entry.IpAddress);
    }

    /// <summary>Each operation is recorded under its own action constant.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="expected">The action constant it maps to.</param>
    [Theory]
    [InlineData(MarketplaceOperation.Install, MarketplaceClientAuditActions.ModuleInstallRequested)]
    [InlineData(MarketplaceOperation.Update, MarketplaceClientAuditActions.ModuleUpdateRequested)]
    [InlineData(MarketplaceOperation.Remove, MarketplaceClientAuditActions.ModuleRemoveRequested)]
    [InlineData(MarketplaceOperation.Rollback, MarketplaceClientAuditActions.ModuleRollbackRequested)]
    public async Task Each_operation_is_recorded_under_its_own_action(MarketplaceOperation operation, string expected)
    {
        await _journal.RecordRequestAsync(
            operation,
            new ModulePackageReference("mail", null, null),
            failure: null,
            string.Empty,
            string.Empty,
            CancellationToken.None);

        Assert.Equal(expected, Written().Action);
    }

    /// <summary>A refused request is recorded as failed with the reason named by the error's code.</summary>
    [Fact]
    public async Task A_refused_request_is_recorded_as_failed_with_the_reason_code()
    {
        await _journal.RecordRequestAsync(
            MarketplaceOperation.Update,
            new ModulePackageReference("mail", "1.3.0", null),
            Error.Of("PackageRevoked", ErrorType.Conflict),
            string.Empty,
            string.Empty,
            CancellationToken.None);

        var entry = Written();
        Assert.False(entry.Succeeded);
        Assert.Equal("module=mail;version=1.3.0;digest=-;reason=PackageRevoked", entry.Subject);
    }

    /// <summary>A failure whose code is not in the reason vocabulary is recorded as unclassified, not as its code.</summary>
    [Fact]
    public async Task A_failure_outside_the_reason_vocabulary_is_recorded_as_unclassified()
    {
        await _journal.RecordRequestAsync(
            MarketplaceOperation.Install,
            new ModulePackageReference("mail", "1.0.0", null),
            Error.Of("https://evil.example/leak?token=abc", ErrorType.Failure),
            string.Empty,
            string.Empty,
            CancellationToken.None);

        var entry = Written();
        Assert.EndsWith(";reason=Unclassified", entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("evil", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>
    /// Planted check: a module id, a version and a digest that are URLs, a lease and a grant leave no trace
    /// of themselves in the entry, whichever field they were passed in.
    /// </summary>
    [Fact]
    public async Task A_url_passed_in_any_field_leaves_no_trace_in_the_entry()
    {
        const string Planted = "https://api-marketplace-maran.innovayse.com/downloads/grant-123?lease=SECRET-LEASE";

        await _journal.RecordRequestAsync(
            MarketplaceOperation.Install,
            new ModulePackageReference(Planted, Planted, Planted),
            Error.Of(Planted, ErrorType.Failure),
            string.Empty,
            string.Empty,
            CancellationToken.None);

        var entry = Written();
        Assert.Equal("module=invalid;version=invalid;digest=invalid;reason=Unclassified", entry.Subject);
        Assert.DoesNotContain("http", entry.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET", entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("grant", entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("lease", entry.Subject, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The inverse control: well-formed facts are written as they are, so the planted check is not a journal that writes nothing.</summary>
    [Fact]
    public async Task Well_formed_facts_are_written_as_they_are()
    {
        await _journal.RecordRequestAsync(
            MarketplaceOperation.Install,
            new ModulePackageReference("mail2", "1.0.0-beta.1+build5", Digest),
            failure: null,
            string.Empty,
            string.Empty,
            CancellationToken.None);

        Assert.Equal($"module=mail2;version=1.0.0-beta.1+build5;digest={Digest}", Written().Subject);
    }

    /// <summary>A digest with upper-case or short text is not a digest and is written as invalid.</summary>
    [Fact]
    public async Task A_digest_that_is_not_lower_case_hexadecimal_is_written_as_invalid()
    {
        await _journal.RecordRequestAsync(
            MarketplaceOperation.Install,
            new ModulePackageReference("mail", "1.0.0", new string('A', 64)),
            failure: null,
            string.Empty,
            string.Empty,
            CancellationToken.None);
        await _journal.RecordRequestAsync(
            MarketplaceOperation.Install,
            new ModulePackageReference("mail", "1.0.0", "abc"),
            failure: null,
            string.Empty,
            string.Empty,
            CancellationToken.None);

        Assert.All(_writer.Entries, entry =>
        {
            Assert.EndsWith("digest=invalid", entry.Subject, StringComparison.Ordinal);
        });
    }

    /// <summary>Switching the marketplace on or off is recorded under its own action, with the reason when refused.</summary>
    [Fact]
    public async Task Switching_the_marketplace_is_recorded_and_a_refusal_carries_its_reason()
    {
        await _journal.RecordSwitchAsync(enabled: true, failure: null, string.Empty, string.Empty, CancellationToken.None);
        await _journal.RecordSwitchAsync(
            enabled: false,
            Error.Of("ReauthenticationFailed", ErrorType.Validation),
            string.Empty,
            string.Empty,
            CancellationToken.None);

        Assert.Equal(MarketplaceClientAuditActions.MarketplaceEnabled, _writer.Entries[0].Action);
        Assert.Equal("switch=on", _writer.Entries[0].Subject);
        Assert.True(_writer.Entries[0].Succeeded);
        Assert.Equal(MarketplaceClientAuditActions.MarketplaceDisabled, _writer.Entries[1].Action);
        Assert.Equal("switch=off;reason=ReauthenticationFailed", _writer.Entries[1].Subject);
        Assert.False(_writer.Entries[1].Succeeded);
    }

    /// <summary>A verification failure is recorded as a failed entry under its own action, naming why.</summary>
    [Fact]
    public async Task A_verification_failure_is_recorded_with_its_reason()
    {
        await _journal.RecordVerificationFailedAsync(
            Error.Of("CatalogueStale", ErrorType.Unavailable),
            string.Empty,
            string.Empty,
            CancellationToken.None);

        var entry = Written();
        Assert.Equal(MarketplaceClientAuditActions.CatalogueVerificationFailed, entry.Action);
        Assert.Equal("answer=catalogue;reason=CatalogueStale", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>
    /// Every reason the vocabulary names is a real error code, either of this module's table or of the two
    /// the Sdk and Identity own, so a failure maps to its reason by name and no reason is a typo.
    /// </summary>
    [Fact]
    public void Every_reason_is_the_name_of_a_real_error_code()
    {
        var moduleCodes = new ResourceManager(
            "Maran.Modules.Marketplace.Resources.ErrorMessages",
            typeof(MarketplaceModule).Assembly);
        var ownedElsewhere = new[] { "ReauthenticationFailed", "ModulePackageNotAsAnnounced" };

        var reasons = Enum.GetNames<MarketplaceAuditReason>()
            .Where(name => { return name is not ("None" or "Unclassified"); })
            .ToList();

        Assert.NotEmpty(reasons);
        Assert.All(reasons, name =>
        {
            Assert.True(
                ownedElsewhere.Contains(name) || moduleCodes.GetString(name, CultureInfo.InvariantCulture) is not null,
                $"{name} is a reason but no error table defines a code of that name.");
        });
        Assert.Contains("None", Enum.GetNames<MarketplaceAuditReason>());
        Assert.Contains("Unclassified", Enum.GetNames<MarketplaceAuditReason>());
    }
}
