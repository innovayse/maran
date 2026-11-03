using Maran.Modules.Cron.Domain.Policies;

namespace Maran.Modules.Cron.Tests.Domain.Policies;

/// <summary>What of a run's output the panel stores.</summary>
public sealed class CronRunOutputPolicyTests
{
    /// <summary>Every NUL character is removed and the rest is returned unchanged.</summary>
    [Fact]
    public void Every_nul_character_is_removed_and_the_rest_is_unchanged()
    {
        Assert.Equal("a b\nc", CronRunOutputPolicy.Storable("a\0 b\0\nc\0"));
    }

    /// <summary>Output with no NUL is returned as it is, including text of other scripts.</summary>
    [Fact]
    public void Output_without_a_nul_is_returned_as_it_is()
    {
        const string Output = "готово աբ ok\n";

        Assert.Equal(Output, CronRunOutputPolicy.Storable(Output));
    }
}
