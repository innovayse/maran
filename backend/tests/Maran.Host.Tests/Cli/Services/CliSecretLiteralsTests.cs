using Maran.Host.Cli.Services;
using Microsoft.Extensions.Configuration;

namespace Maran.Host.Tests.Cli.Services;

/// <summary>The secret literals are read from raw configuration, so a verb needing no secret runs without any.</summary>
public sealed class CliSecretLiteralsTests
{
    /// <summary>A panel with no secret configured yields an empty list instead of refusing (a key-free verb must run).</summary>
    [Fact]
    public void Reading_secrets_with_none_configured_returns_an_empty_list()
    {
        var literals = new CliSecretLiterals(new ConfigurationBuilder().Build());

        Assert.Empty(literals.Current());
    }

    /// <summary>A configured encryption key is listed (positive control for the empty case above).</summary>
    [Fact]
    public void Reading_secrets_lists_a_configured_value()
    {
        var literals = new CliSecretLiterals(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:EncryptionKey"] = "planted-key-0001" })
                .Build());

        Assert.Equal(["planted-key-0001"], literals.Current());
    }
}
