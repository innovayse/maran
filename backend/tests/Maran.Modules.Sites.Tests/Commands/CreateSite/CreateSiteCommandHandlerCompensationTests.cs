using Maran.Modules.Sites.Commands.CreateSite;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Sites.Tests.Commands.CreateSite;

/// <summary>
/// Behavioral contract of <see cref="CreateSiteCommandHandler"/> when the host and the database
/// disagree: the agent refusing the creation, a rival winning the hostname key or the slot gate
/// refusing late, and the compensating removal of the vhost (withheld when the vhost is the
/// winner's) that follows.
/// </summary>
public sealed class CreateSiteCommandHandlerCompensationTests
{
    /// <summary>A host that already serves the name is answered as a taken domain and nothing is removed.</summary>
    [Fact]
    public async Task A_host_that_already_serves_the_name_is_answered_as_a_taken_domain_and_nothing_is_removed()
    {
        // The agent refused the creation without writing a vhost, so this request has nothing to undo:
        // a delete here would remove the vhost of whichever site DOES own the name.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, createFailure: Error.Of("AgentAlreadyExists", ErrorType.Conflict));

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.StartsWith("create:", Assert.Single(world.Agent.Calls), StringComparison.Ordinal);
        Assert.Empty(await world.DbContext.Sites.ToListAsync());
        var entry = Assert.Single(world.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("example.com", entry.Subject);
    }

    /// <summary>An agent limit refusal keeps its own code although it is also a conflict.</summary>
    [Fact]
    public async Task An_agent_limit_refusal_keeps_its_own_code_although_it_is_also_a_conflict()
    {
        // The inverse control of the test above: the re-reading is by the agent's CODE, so another
        // refusal of the same KIND must not be told to the customer as "this domain is taken".
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, createFailure: Error.Of("AgentLimitReached", ErrorType.Conflict));

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentLimitReached", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    /// <summary>A creation that loses a hostname at the insert is told the domain is taken and its vhost is removed.</summary>
    [Fact]
    public async Task A_creation_that_loses_a_hostname_at_the_insert_is_told_the_domain_is_taken_and_its_vhost_is_removed()
    {
        // The race itself: both creations passed the pre-check, the other one committed shop.example
        // first, and this one's alias hits the hostname key. Its vhost is live on the host naming a
        // hostname another row owns, so it must go and the answer must be a 409, never a 500.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        world.LoseTo("winner.example", ["shop.example"]);

        var result = await world.Handler().HandleAsync(
            CreateSiteWorld.Command(account, "zero.example", ["shop.example"]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(
            ["create:acme:zero.example:Php:8.3:shop.example:10", "delete:acme:zero.example:"],
            world.Agent.Calls);
        Assert.Equal(["winner.example"], await world.AllDomainsAsync());
        var entry = Assert.Single(world.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("zero.example", entry.Subject);
    }

    /// <summary>A loser whose domain is only another sites alias still has its own vhost removed.</summary>
    [Fact]
    public async Task A_loser_whose_domain_is_only_another_sites_alias_still_has_its_own_vhost_removed()
    {
        // The vhost file is named after the PRIMARY domain, so a claim held as an alias names a
        // different file: the loser's own file is nobody's and goes.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        world.LoseTo("winner.example", ["www.shop.example"]);

        var result = await world.Handler().HandleAsync(
            CreateSiteWorld.Command(account, "www.shop.example", []), CancellationToken.None);

        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.Contains("delete:acme:www.shop.example:", world.Agent.Calls);
    }

    /// <summary>A loser of the same primary domain in another account leaves the winners vhost in place.</summary>
    [Fact]
    public async Task A_loser_of_the_same_primary_domain_leaves_the_winners_vhost_in_place()
    {
        // Both requests wrote the one file, and the winner's row owns it now. Deleting it would take
        // the winner's site off the air, so the delete is withheld and reported at Error. The winner
        // belongs to ANOTHER account, so the question is only answerable with the tenant filter off.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        world.LoseTo("shop.example", []);

        var result = await world.Handler().HandleAsync(
            CreateSiteWorld.Command(account, "shop.example", []), CancellationToken.None);

        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.DoesNotContain(world.Agent.Calls, call => { return call.StartsWith("delete:", StringComparison.Ordinal); });
        var logged = Assert.Single(world.Logger.Entries);
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.Equal(2, logged.EventId);
    }

    /// <summary>The ownership question ignores case.</summary>
    [Fact]
    public async Task The_ownership_question_ignores_case()
    {
        // Host matching and the agent's file name are both lower-cased, so Shop.Example and
        // shop.example are one vhost file and the delete must be withheld for either spelling.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        world.LoseTo("shop.example", []);

        await world.Handler().HandleAsync(CreateSiteWorld.Command(account, "Shop.Example", []), CancellationToken.None);

        Assert.DoesNotContain(world.Agent.Calls, call => { return call.StartsWith("delete:", StringComparison.Ordinal); });
    }

    /// <summary>A row refused for another reason removes the vhost and surfaces as a fault.</summary>
    [Fact]
    public async Task A_row_refused_for_another_reason_removes_the_vhost_and_surfaces_as_a_fault()
    {
        // A dropped connection says nothing about the request: it must not be reported as "this domain
        // is taken", and the vhost goes because the refused insert proves nothing was committed.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        world.SlotGate.ThrowOnNextClaim = GateFailures.ConnectionFailure();

        await Assert.ThrowsAsync<DbUpdateException>(
            async () => { await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None); });

        Assert.Equal("delete:acme:example.com:", world.Agent.Calls[^1]);
        Assert.Equal(2, world.Agent.Calls.Count);
    }

    /// <summary>A failure outside the insert is not compensated.</summary>
    [Fact]
    public async Task A_failure_outside_the_insert_is_not_compensated()
    {
        // Whether the row was written is unknown, and deleting the vhost of a site whose row did
        // commit is the reverse disagreement. So the handler propagates and removes nothing.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        world.SlotGate.ThrowOnNextClaim = new InvalidOperationException("commit failed");

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => { await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None); });

        Assert.DoesNotContain(world.Agent.Calls, call => { return call.StartsWith("delete:", StringComparison.Ordinal); });
    }

    /// <summary>A creation the slot gate refuses is removed from the host and reported as the plan filling up.</summary>
    [Fact]
    public async Task A_creation_the_slot_gate_refuses_is_removed_from_the_host_and_reported_as_the_plan_filling_up()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        world.SlotGate.RefuseNextClaim = true;

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteLimitReachedConcurrently", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("delete:acme:example.com:", world.Agent.Calls[^1]);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>A compensation is not abandoned when the caller hangs up.</summary>
    [Fact]
    public async Task A_compensation_is_not_abandoned_when_the_caller_hangs_up()
    {
        // The caller cancels while the insert is in flight and the key then refuses it. The removal
        // the refusal made necessary must still run, so its token is not the request's.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        using var cancellation = new CancellationTokenSource();
        world.LoseTo("winner.example", ["www.example.com"], beforeThrow: cancellation.Cancel);

        await world.Handler().HandleAsync(CreateSiteWorld.Command(account), cancellation.Token);

        Assert.StartsWith("delete:", world.Agent.Calls[^1], StringComparison.Ordinal);
        Assert.False(world.Agent.Tokens[^1].IsCancellationRequested);
        Assert.NotEqual(cancellation.Token, world.Agent.Tokens[^1]);
    }

    /// <summary>A compensation that fails is reported to the operator and the customer still gets the taken-domain answer.</summary>
    [Fact]
    public async Task A_compensation_that_fails_is_logged_and_the_customer_still_gets_the_taken_domain_answer()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, deleteFailure: Error.Of("AgentSystemFailure", ErrorType.Failure));
        world.LoseTo("winner.example", ["www.example.com"]);

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        var logged = Assert.Single(world.Logger.Entries);
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.Equal(1, logged.EventId);
        Assert.Contains("AgentSystemFailure", logged.Message, StringComparison.Ordinal);
    }

    /// <summary>Two creations with disjoint names both succeed and nothing is removed.</summary>
    [Fact]
    public async Task Two_creations_with_disjoint_names_both_succeed_and_nothing_is_removed()
    {
        // The inverse control for the compensation tests: a handler mutated to compensate (or to
        // refuse) whenever a rival committed during the host work would pass every test above, which
        // only ever hand it a rival that collides.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        world.CommitRivalBeforeClaim("rival.example", ["www.rival.example"]);

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(world.Agent.Calls, call => { return call.StartsWith("delete:", StringComparison.Ordinal); });
        Assert.Equal(["example.com", "rival.example"], await world.AllDomainsAsync());
    }
}
