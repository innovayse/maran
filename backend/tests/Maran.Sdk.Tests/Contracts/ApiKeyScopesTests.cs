using Maran.Sdk.Contracts;

namespace Maran.Sdk.Tests.Contracts;

/// <summary>The closed scope vocabulary and who may be granted which part of it.</summary>
public sealed class ApiKeyScopesTests
{
    /// <summary>The two provisioning write scopes are reseller-grade: a reseller may hold them, the administrator may not.</summary>
    [Fact]
    public void The_write_scopes_are_grantable_to_a_reseller_and_never_to_the_administrator()
    {
        foreach (var scope in new[] { ApiKeyScopes.AccountsWrite, ApiKeyScopes.AccountsDelete })
        {
            Assert.Contains(scope, ApiKeyScopes.ResellerGrantable);
            Assert.Contains(scope, ApiKeyScopes.All);
            Assert.DoesNotContain(scope, ApiKeyScopes.AdministratorGrantable);
            Assert.Contains(scope, ApiKeyScopes.GrantableTo(Guid.NewGuid()));
            Assert.DoesNotContain(scope, ApiKeyScopes.GrantableTo(null));
        }
    }

    /// <summary>Deleting is a scope of its own, distinct from every other scope, so holding the others never implies it.</summary>
    [Fact]
    public void Delete_is_its_own_scope_distinct_from_write_and_read()
    {
        Assert.Equal("accounts.write", ApiKeyScopes.AccountsWrite);
        Assert.Equal("accounts.delete", ApiKeyScopes.AccountsDelete);
        Assert.Equal(ApiKeyScopes.All.Count, ApiKeyScopes.All.Distinct().Count());
        Assert.NotEqual(ApiKeyScopes.AccountsWrite, ApiKeyScopes.AccountsDelete);
        Assert.NotEqual(ApiKeyScopes.AccountsRead, ApiKeyScopes.AccountsWrite);
    }

    /// <summary>Every scope is grantable to some owner, and the two owners share only the implicit self scope.</summary>
    [Fact]
    public void Every_scope_is_grantable_and_the_owners_share_only_the_self_scope()
    {
        var grantable = ApiKeyScopes.AdministratorGrantable.Concat(ApiKeyScopes.ResellerGrantable).ToHashSet();

        Assert.True(ApiKeyScopes.All.All(grantable.Contains));
        Assert.Equal(
            [ApiKeyScopes.SelfRead],
            ApiKeyScopes.AdministratorGrantable.Intersect(ApiKeyScopes.ResellerGrantable).ToList());
    }
}
