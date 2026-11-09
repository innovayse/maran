using Maran.Modules.Backups.Services;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>The key a destination's archives are sealed with has the shape the agent accepts, and is not repeated.</summary>
public sealed class ArchiveKeyGeneratorTests
{
    /// <summary>The agent's <c>ArchiveKey</c> takes exactly 64 lowercase hexadecimal characters.</summary>
    [Fact]
    public void A_key_is_64_lowercase_hexadecimal_characters()
    {
        var key = new ArchiveKeyGenerator().Mint();

        Assert.Equal(ArchiveKeyGenerator.KeyBytes * 2, key.Length);
        Assert.Matches("^[0-9a-f]{64}$", key);
    }

    /// <summary>Two keys are not the same key; a generator that repeated would seal every destination under one.</summary>
    [Fact]
    public void Two_keys_differ()
    {
        var generator = new ArchiveKeyGenerator();

        Assert.NotEqual(generator.Mint(), generator.Mint());
    }
}
