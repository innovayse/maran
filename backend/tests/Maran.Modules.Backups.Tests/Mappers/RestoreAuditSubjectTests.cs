using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Mappers;

namespace Maran.Modules.Backups.Tests.Mappers;

/// <summary>What a restore's audit entry names: the account, and the one part when it was narrowed.</summary>
public sealed class RestoreAuditSubjectTests
{
    /// <summary>A whole restore is recorded under the account alone — what it has always been.</summary>
    [Fact]
    public void A_whole_restore_is_recorded_under_the_account_alone()
    {
        Assert.Equal("alice", RestoreAuditSubject.Of("alice", new AgentWholeRestoreScope()));
    }

    /// <summary>A database restore names the database after the account.</summary>
    [Fact]
    public void A_database_restore_names_the_database_after_the_account()
    {
        Assert.Equal(
            "alice/database/alice_shop",
            RestoreAuditSubject.Of("alice", new AgentDatabaseRestoreScope("alice_shop")));
    }

    /// <summary>A site restore names the domain after the account, and never a path.</summary>
    [Fact]
    public void A_site_restore_names_the_domain_after_the_account_and_never_a_path()
    {
        var subject = RestoreAuditSubject.Of("alice", new AgentSiteRestoreScope("shop.example.com"));

        Assert.Equal("alice/site/shop.example.com", subject);
        Assert.DoesNotContain("/home", subject, StringComparison.Ordinal);
    }

    /// <summary>The account always leads, so a search for the account finds every restore of it.</summary>
    [Fact]
    public void The_account_always_leads_the_subject()
    {
        foreach (AgentRestoreScope scope in new AgentRestoreScope[]
        {
            new AgentWholeRestoreScope(),
            new AgentDatabaseRestoreScope("alice_shop"),
            new AgentSiteRestoreScope("shop.example.com"),
        })
        {
            Assert.StartsWith("alice", RestoreAuditSubject.Of("alice", scope), StringComparison.Ordinal);
        }
    }
}
