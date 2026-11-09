using Maran.Modules.Mail.Commands.RotateDkimKey;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.RotateDkimKey;

/// <summary>
/// Rotating a DKIM key: a fresh selector, the old key kept as retiring, the private key only as
/// ciphertext, and the host put back when the new key cannot be recorded.
/// </summary>
public sealed class RotateDkimKeyCommandHandlerTests
{
    /// <summary>The account that owns the domain in these tests.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>Another account whose context must not reach the first one's domain.</summary>
    private static readonly Guid OtherAccountId = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>Rotation installs a new selector on the host, keeps the old key retiring and journals it.</summary>
    [Fact]
    public async Task Rotating_installs_a_new_selector_and_keeps_the_old_key_retiring()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "example.com");

        var result = await DomainGroupHandlers.Rotate(context).HandleAsync(
            new RotateDkimKeyCommand(domain.Id, "10.0.0.1", "agent/1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("m20260909-2", result.Value.DkimSelector);
        Assert.Equal(["ConfigureDomain(acme,example.com,m20260909-2)"], context.Agent.Calls);

        var keys = (await context.Database.DkimKeys.ToListAsync()).OrderBy(key =>
        {
            return key.Selector;
        }).ToList();
        Assert.Equal(2, keys.Count);
        Assert.Equal(("m20261107-1", DkimKeyState.Retiring), (keys[1].Selector, keys[1].State));
        Assert.Equal(("m20260909-2", DkimKeyState.Active), (keys[0].Selector, keys[0].State));
        Assert.NotNull(keys[1].RetiredAt);

        var entry = Assert.Single(context.Entries);
        Assert.Equal("MailDkimKeyRotated", entry.Action);
        Assert.Equal("example.com", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Rotating twice in one day yields two different selectors and only the latest key stays active.</summary>
    [Fact]
    public async Task Rotating_twice_in_a_day_yields_distinct_selectors_and_one_active_key()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "example.com");
        var handler = DomainGroupHandlers.Rotate(context);

        var first = await handler.HandleAsync(new RotateDkimKeyCommand(domain.Id), CancellationToken.None);
        var second = await handler.HandleAsync(new RotateDkimKeyCommand(domain.Id), CancellationToken.None);

        Assert.Equal("m20260909-2", first.Value.DkimSelector);
        Assert.Equal("m20260909-3", second.Value.DkimSelector);
        var keys = await context.Database.DkimKeys.ToListAsync();
        Assert.Equal(3, keys.Count);
        Assert.Single(keys, key =>
        {
            return key.State == DkimKeyState.Active;
        });
        Assert.Single(keys, key =>
        {
            return key.State == DkimKeyState.Retiring;
        });
        Assert.Single(keys, key =>
        {
            return key.State == DkimKeyState.Retired;
        });
    }

    /// <summary>The new private key is stored only as ciphertext and appears in no DTO, audit entry or call string.</summary>
    [Fact]
    public async Task The_new_private_key_is_stored_only_as_ciphertext_and_never_surfaces()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "example.com");

        var result = await DomainGroupHandlers.Rotate(context).HandleAsync(
            new RotateDkimKeyCommand(domain.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var active = await context.Database.DkimKeys.SingleAsync(key => key.State == DkimKeyState.Active);
        Assert.StartsWith(ReversibleEncryption.Marker, active.PrivateKeyEncrypted, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-1", result.Value.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(MailDomainDto).GetProperties(),
            property =>
            {
                return property.Name.Contains("Private", StringComparison.OrdinalIgnoreCase);
            });
        Assert.All(
            context.Entries,
            entry =>
            {
                Assert.DoesNotContain("fake-1", entry.Subject, StringComparison.Ordinal);
            });
        Assert.All(
            context.Agent.Calls,
            call =>
            {
                Assert.DoesNotContain("fake-1", call, StringComparison.Ordinal);
            });
    }

    /// <summary>An agent failure leaves the keys untouched and is journaled as a failure.</summary>
    [Fact]
    public async Task An_agent_failure_leaves_the_keys_untouched()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "example.com");
        context.Agent.FailNext("ConfigureDomain", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await DomainGroupHandlers.Rotate(context).HandleAsync(
            new RotateDkimKeyCommand(domain.Id), CancellationToken.None);

        Assert.Equal("AgentUnavailable", result.Error!.Code);
        var key = await context.Database.DkimKeys.SingleAsync();
        Assert.Equal(DkimKeyState.Active, key.State);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>A row that cannot be written puts the previous key back on the host.</summary>
    [Fact]
    public async Task A_row_that_cannot_be_written_puts_the_previous_key_back_on_the_host()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "example.com");
        context.SaveFailure.Next = new DbUpdateException("save failed", new InvalidOperationException("db down"));

        var result = await DomainGroupHandlers.Rotate(context).HandleAsync(
            new RotateDkimKeyCommand(domain.Id), CancellationToken.None);

        Assert.Equal("MailDomainProvisioningFailed", result.Error!.Code);
        Assert.Equal(
            ["ConfigureDomain(acme,example.com,m20260909-2)",
                "ConfigureDomain(acme,example.com,m20261107-1)",],
            context.Agent.Calls);
        Assert.Equal("key", context.Agent.LastDomain!.DkimPrivateKeyPem.Reveal());
        var key = await context.Database.DkimKeys.SingleAsync();
        Assert.Equal("m20261107-1", key.Selector);
        Assert.Equal(DkimKeyState.Active, key.State);
    }

    /// <summary>A domain that does not exist is not found.</summary>
    [Fact]
    public async Task A_domain_that_does_not_exist_is_not_found()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());

        var result = await DomainGroupHandlers.Rotate(context).HandleAsync(
            new RotateDkimKeyCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("MailDomainNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>Another account's domain answers not found and keeps its key; the owner's own context rotates it.</summary>
    [Fact]
    public async Task Another_accounts_domain_answers_not_found_and_keeps_its_key()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = new MailTestContext(new CustomerCurrentUser(AccountId), databaseName);
        owner.Accounts.Add(AccountId, "acme");
        var domain = await owner.SeedDomainAsync(AccountId, "example.com");

        using var intruder = new MailTestContext(new CustomerCurrentUser(OtherAccountId), databaseName);
        intruder.Accounts.Add(OtherAccountId, "beta");
        var refused = await DomainGroupHandlers.Rotate(intruder).HandleAsync(
            new RotateDkimKeyCommand(domain.Id), CancellationToken.None);

        Assert.Equal("MailDomainNotFound", refused.Error!.Code);
        Assert.Empty(intruder.Agent.Calls);
        Assert.Single(await owner.Database.DkimKeys.ToListAsync());

        var accepted = await DomainGroupHandlers.Rotate(owner).HandleAsync(
            new RotateDkimKeyCommand(domain.Id), CancellationToken.None);

        Assert.True(accepted.IsSuccess);
        Assert.Equal(2, (await owner.Database.DkimKeys.ToListAsync()).Count);
    }
}
