using Maran.Sdk.Contracts;
using Maran.Sdk.Manifests;

namespace Maran.Sdk.Tests.Manifests;

/// <summary>The vocabulary of permission names is the Sdk's policy constants, read from the classes themselves.</summary>
public sealed class ManifestPermissionVocabularyTests
{
    /// <summary>Every role policy and every API-key policy the Sdk names is in the vocabulary.</summary>
    [Fact]
    public void Every_policy_the_Sdk_names_is_in_the_vocabulary()
    {
        string[] named =
        [
            AuthorizationPolicies.AdminOnly,
            AuthorizationPolicies.ResellerOnly,
            AuthorizationPolicies.AdminOrReseller,
            AuthorizationPolicies.AnyAuthenticated,
            ApiKeyPolicyNames.SelfRead,
            ApiKeyPolicyNames.ServerRead,
            ApiKeyPolicyNames.PlansRead,
            ApiKeyPolicyNames.AccountsRead,
            ApiKeyPolicyNames.AccountsWrite,
            ApiKeyPolicyNames.AccountsDelete,
        ];

        foreach (var name in named)
        {
            Assert.Contains(name, ManifestPermissionVocabulary.Policies);
        }
    }

    /// <summary>Names the Sdk does not define are not in the vocabulary, and the vocabulary is not empty.</summary>
    [Fact]
    public void A_name_the_Sdk_does_not_define_is_not_in_the_vocabulary()
    {
        Assert.NotEmpty(ManifestPermissionVocabulary.Policies);
        Assert.DoesNotContain("SuperUser", ManifestPermissionVocabulary.Policies);
        Assert.DoesNotContain(string.Empty, ManifestPermissionVocabulary.Policies);
        Assert.DoesNotContain("adminonly", ManifestPermissionVocabulary.Policies);
    }
}
