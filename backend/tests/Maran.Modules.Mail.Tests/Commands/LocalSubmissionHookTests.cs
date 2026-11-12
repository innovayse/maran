using Maran.Modules.Mail.Commands.CreateMailAlias;
using Maran.Modules.Mail.Commands.CreateMailbox;
using Maran.Modules.Mail.Commands.DeleteMailAlias;
using Maran.Modules.Mail.Commands.DeleteMailbox;
using Maran.Modules.Mail.Commands.DeleteMailDomain;
using Maran.Modules.Mail.Commands.UpdateMailAlias;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Commands;

/// <summary>
/// Adding or removing a mailbox, a domain or an alias pushes the account's complete local submission binding once the
/// change is committed, and a refusal by the agent never fails the committed change.
/// </summary>
public sealed class LocalSubmissionHookTests
{
    /// <summary>The account these tests change.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>Creating a mailbox pushes a binding that lists the new login.</summary>
    [Fact]
    public async Task Creating_a_mailbox_pushes_a_binding_that_lists_the_new_login()
    {
        using var context = await ArrangeAsync();
        var domain = await context.SeedDomainAsync(AccountId, "example.com");

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512, "203.0.113.7", "agent/1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(context.Agent.LocalSubmissions);
        Assert.Equal(["example.com"], sent.Domains);
        Assert.Equal(["info@example.com"], sent.MailboxLogins);
    }

    /// <summary>A refused creation pushes nothing, and a refused push does not fail a committed creation.</summary>
    [Fact]
    public async Task A_refused_creation_pushes_nothing_and_a_refused_push_does_not_fail_a_committed_one()
    {
        using var refused = await ArrangeAsync();
        var missing = await MailboxGroupHandlers.Create(refused).HandleAsync(
            new CreateMailboxCommand(Guid.NewGuid(), "info", MailboxGroupSeeds.Password, 512, "203.0.113.7", "agent/1"),
            CancellationToken.None);

        using var pushRefused = await ArrangeAsync();
        var domain = await pushRefused.SeedDomainAsync(AccountId, "example.com");
        pushRefused.Agent.FailNext("SetLocalSubmission", Error.Of("AgentInvalidInput", ErrorType.Validation));
        var created = await MailboxGroupHandlers.Create(pushRefused).HandleAsync(
            new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512, "203.0.113.7", "agent/1"),
            CancellationToken.None);

        Assert.False(missing.IsSuccess);
        Assert.Empty(refused.Agent.LocalSubmissions);
        Assert.True(created.IsSuccess);
        Assert.Single(pushRefused.Database.Mailboxes);
    }

    /// <summary>Deleting a mailbox pushes a binding without its login; the other login stays.</summary>
    [Fact]
    public async Task Deleting_a_mailbox_pushes_a_binding_without_its_login()
    {
        using var context = await ArrangeAsync();
        var domain = await context.SeedDomainAsync(AccountId, "example.com");
        var gone = await context.SeedMailboxAsync(domain, "info");
        await context.SeedMailboxAsync(domain, "sales");

        var result = await MailboxGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailboxCommand(gone.Id, string.Empty, string.Empty), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["sales@example.com"], Assert.Single(context.Agent.LocalSubmissions).MailboxLogins);
    }

    /// <summary>Deleting a domain pushes a binding without the domain and its logins.</summary>
    [Fact]
    public async Task Deleting_a_domain_pushes_a_binding_without_the_domain_and_its_logins()
    {
        using var context = await ArrangeAsync();
        var gone = await context.SeedDomainAsync(AccountId, "gone.test");
        var kept = await context.SeedDomainAsync(AccountId, "kept.test");
        await context.SeedMailboxAsync(gone, "info");
        await context.SeedMailboxAsync(kept, "info");

        var result = await DomainGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailDomainCommand(gone.Id, string.Empty, string.Empty), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(context.Agent.LocalSubmissions);
        Assert.Equal(["kept.test"], sent.Domains);
        Assert.Equal(["info@kept.test"], sent.MailboxLogins);
    }

    /// <summary>Creating a forwarding alias pushes a binding that carries its outside delivery count at once.</summary>
    [Fact]
    public async Task Creating_a_forwarder_pushes_a_binding_with_its_outside_delivery_count()
    {
        using var context = await ArrangeForwardingAsync();
        var domain = await context.SeedDomainAsync(AccountId, "example.com");

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            new CreateMailAliasCommand(
                domain.Id, MailAliasKind.Forwarder, "sales", ["a@outside.test", "b@outside.test"], "203.0.113.7", "agent/1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fanout = Assert.Single(Assert.Single(context.Agent.LocalSubmissions).AliasFanouts);
        Assert.Equal("sales@example.com", fanout.Alias);
        Assert.Equal(2u, fanout.ExternalTargets);
    }

    /// <summary>Inverse control: a refused alias pushes nothing, and a refused push does not fail a committed alias.</summary>
    [Fact]
    public async Task A_refused_alias_pushes_nothing_and_a_refused_push_does_not_fail_a_committed_one()
    {
        using var refused = await ArrangeAsync();
        var missing = await MailAliasHandlers.Create(refused).HandleAsync(
            new CreateMailAliasCommand(
                Guid.NewGuid(), MailAliasKind.Alias, "info", ["bob@example.com"], "203.0.113.7", "agent/1"),
            CancellationToken.None);

        using var pushRefused = await ArrangeAsync();
        var domain = await pushRefused.SeedDomainAsync(AccountId, "example.com");
        pushRefused.Agent.FailNext("SetLocalSubmission", Error.Of("AgentInvalidInput", ErrorType.Validation));
        var created = await MailAliasHandlers.Create(pushRefused).HandleAsync(
            new CreateMailAliasCommand(
                domain.Id, MailAliasKind.Alias, "info", ["bob@example.com"], "203.0.113.7", "agent/1"),
            CancellationToken.None);

        Assert.False(missing.IsSuccess);
        Assert.Empty(refused.Agent.LocalSubmissions);
        Assert.True(created.IsSuccess);
        Assert.Single(pushRefused.Database.MailAliases);
    }

    /// <summary>Growing an alias's target list pushes the new, larger figure at once.</summary>
    [Fact]
    public async Task Updating_an_alias_pushes_the_new_outside_delivery_count()
    {
        using var context = await ArrangeForwardingAsync();
        var domain = await context.SeedDomainAsync(AccountId, "example.com");
        var alias = await MailAliasSeeds.SeedAliasAsync(
            context, domain, "sales", MailAliasKind.Forwarder, "a@outside.test");

        var result = await MailAliasHandlers.Update(context).HandleAsync(
            new UpdateMailAliasCommand(
                ["a@outside.test", "b@outside.test", "c@outside.test"], alias.Id, "203.0.113.7", "agent/1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fanout = Assert.Single(Assert.Single(context.Agent.LocalSubmissions).AliasFanouts);
        Assert.Equal(3u, fanout.ExternalTargets);
    }

    /// <summary>Deleting an alias pushes a binding without it, so its cost falls back at once.</summary>
    [Fact]
    public async Task Deleting_an_alias_pushes_a_binding_without_it()
    {
        using var context = await ArrangeAsync();
        var domain = await context.SeedDomainAsync(AccountId, "example.com");
        var gone = await MailAliasSeeds.SeedAliasAsync(
            context, domain, "sales", MailAliasKind.Forwarder, "a@outside.test", "b@outside.test");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "keep", MailAliasKind.Forwarder, "c@outside.test");

        var result = await MailAliasHandlers.Delete(context).HandleAsync(
            new DeleteMailAliasCommand(gone.Id, "203.0.113.7", "agent/1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fanout = Assert.Single(Assert.Single(context.Agent.LocalSubmissions).AliasFanouts);
        Assert.Equal("keep@example.com", fanout.Alias);
    }

    /// <summary>Builds a context with mail on, external forwarding allowed and one account.</summary>
    /// <returns>The context.</returns>
    private static async Task<MailTestContext> ArrangeForwardingAsync()
    {
        var context = new MailTestContext();
        await MailAliasSeeds.SeedServerAsync(context, allowExternalForwarding: true);
        context.Accounts.Add(AccountId, "acme");

        return context;
    }

    /// <summary>Builds a context with mail on and one account.</summary>
    /// <returns>The context.</returns>
    private static async Task<MailTestContext> ArrangeAsync()
    {
        var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme");

        return context;
    }
}
