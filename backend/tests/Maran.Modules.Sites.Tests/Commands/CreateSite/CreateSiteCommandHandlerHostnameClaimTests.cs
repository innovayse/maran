using Maran.Modules.Sites.Commands.CreateSite;
using Maran.Modules.Sites.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Commands.CreateSite;

/// <summary>
/// Behavioral contract of <see cref="CreateSiteCommandHandler"/> for hostname uniqueness: a domain
/// or alias another tenant already serves is refused before the agent is called, whichever spelling
/// or direction the clash takes, and a created site claims every name it serves.
/// </summary>
public sealed class CreateSiteCommandHandlerHostnameClaimTests
{
    /// <summary>A domain already served for another tenant is still taken.</summary>
    [Fact]
    public async Task A_domain_already_served_for_another_tenant_is_still_taken()
    {
        // The uniqueness check must ignore the tenant filter: a domain is claimed once per SERVER.
        // With the filter applied the conflicting row would be invisible, the check would pass, and
        // the insert would blow up on the unique index instead of answering a typed 409.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        await using (var otherTenant = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            otherTenant.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "example.com"));
            await otherTenant.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>An alias naming another tenants domain is refused.</summary>
    [Fact]
    public async Task An_alias_naming_another_tenants_domain_is_refused()
    {
        // The domain takeover this uniqueness exists to stop. nginx resolves a request by Host
        // alone, and the include is a sorted glob, so a site whose file sorts first wins every
        // request for a name it claims — the victim's ACME challenge location included, which is a
        // publicly trusted certificate for a domain the requester does not own.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        await using (var otherTenant = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            otherTenant.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "victim.example.com"));
            await otherTenant.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(
            CreateSiteWorld.Command(account, "aaa-attacker.example.com", ["victim.example.com"]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>An alias naming another tenants alias is refused.</summary>
    [Fact]
    public async Task An_alias_naming_another_tenants_alias_is_refused()
    {
        // The same takeover one step further out: two vhosts claiming one name is a takeover
        // whether the name is the victim's primary domain or one of its aliases, because nginx
        // makes no distinction between the two in server_name.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        await using (var otherTenant = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            otherTenant.Sites.Add(
                SitesTestContext.PhpSite(Guid.NewGuid(), "victim.example.com", "8.3", "shop.example.com"));
            await otherTenant.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(
            CreateSiteWorld.Command(account, "aaa-attacker.example.com", ["shop.example.com"]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>A domain naming another tenants alias is refused.</summary>
    [Fact]
    public async Task A_domain_naming_another_tenants_alias_is_refused()
    {
        // The direction the Domain unique index alone cannot see: the conflicting claim is not
        // another site's Domain column, so only a check over the whole claimed set catches it.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        await using (var otherTenant = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            otherTenant.Sites.Add(
                SitesTestContext.PhpSite(Guid.NewGuid(), "victim.example.com", "8.3", "shop.example.com"));
            await otherTenant.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(
            CreateSiteWorld.Command(account, "shop.example.com", []), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>An alias differing only in case from another tenants domain is refused.</summary>
    [Fact]
    public async Task An_alias_differing_only_in_case_from_another_tenants_domain_is_refused()
    {
        // Host matching is case-insensitive, so "Victim.example.com" and "victim.example.com" are
        // one name to nginx and must be one claim here.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);
        await using (var otherTenant = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            otherTenant.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "victim.example.com"));
            await otherTenant.SaveChangesAsync();
        }

        var result = await world.Handler().HandleAsync(
            CreateSiteWorld.Command(account, "aaa-attacker.example.com", ["Victim.Example.com"]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
    }

    /// <summary>A created site claims its domain and every alias.</summary>
    [Fact]
    public async Task A_created_site_claims_its_domain_and_every_alias()
    {
        // The claims are what the next creation is refused against, so a site stored without them
        // would leave every name it serves free for another account to take.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);

        await world.Handler().HandleAsync(
            CreateSiteWorld.Command(account, "example.com", ["www.example.com", "shop.example.com"]), CancellationToken.None);

        var claimed = await world.DbContext.SiteHostnames
            .IgnoreQueryFilters()
            .Select(hostname => hostname.Name)
            .ToListAsync();
        Assert.Equal(
            ["example.com", "shop.example.com", "www.example.com"],
            claimed.OrderBy(
                name =>
                {
                    return name;
                },
                StringComparer.Ordinal));
    }
}
