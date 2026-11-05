using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Domain.Policies;

namespace Maran.Modules.Monitoring.Tests.Domain.Policies;

/// <summary>Behavioral contract of <see cref="PendingUpdateClassificationPolicy"/> (issue #163).</summary>
public sealed class PendingUpdateClassificationPolicyTests
{
    /// <summary>Each reading is worded once, and the wording is one of the words the filter accepts.</summary>
    /// <param name="classification">What the agent measured.</param>
    /// <param name="word">The expected word.</param>
    [Theory]
    [InlineData(AgentUpdateClassification.Security, "security")]
    [InlineData(AgentUpdateClassification.Ordinary, "ordinary")]
    [InlineData(AgentUpdateClassification.Unmeasured, "unmeasured")]
    public void Each_reading_is_worded_once_and_the_word_is_one_the_filter_accepts(
        AgentUpdateClassification classification,
        string word)
    {
        var name = PendingUpdateClassificationPolicy.NameOf(classification);

        Assert.Equal(word, name);
        Assert.Contains(name, PendingUpdateClassificationPolicy.All);
    }

    /// <summary>The vocabulary is exactly the three words, in sort-rank order.</summary>
    [Fact]
    public void The_vocabulary_is_exactly_the_three_words_in_sort_rank_order()
    {
        Assert.Equal(["security", "ordinary", "unmeasured"], PendingUpdateClassificationPolicy.All);
    }

    /// <summary>Security ranks before ordinary, and ordinary before what was not measured.</summary>
    [Fact]
    public void Security_ranks_before_ordinary_and_ordinary_before_unmeasured()
    {
        var security = PendingUpdateClassificationPolicy.RankOf(AgentUpdateClassification.Security);
        var ordinary = PendingUpdateClassificationPolicy.RankOf(AgentUpdateClassification.Ordinary);
        var unmeasured = PendingUpdateClassificationPolicy.RankOf(AgentUpdateClassification.Unmeasured);

        Assert.True(security < ordinary);
        Assert.True(ordinary < unmeasured);
    }

    /// <summary>The word of every reading sits at the position its rank names in the vocabulary.</summary>
    /// <param name="classification">What the agent measured.</param>
    [Theory]
    [InlineData(AgentUpdateClassification.Security)]
    [InlineData(AgentUpdateClassification.Ordinary)]
    [InlineData(AgentUpdateClassification.Unmeasured)]
    public void The_rank_of_a_reading_is_the_position_of_its_word_in_the_vocabulary(
        AgentUpdateClassification classification)
    {
        var word = PendingUpdateClassificationPolicy.NameOf(classification);

        Assert.Equal(
            PendingUpdateClassificationPolicy.All.ToList().IndexOf(word),
            PendingUpdateClassificationPolicy.RankOf(classification));
    }
}
