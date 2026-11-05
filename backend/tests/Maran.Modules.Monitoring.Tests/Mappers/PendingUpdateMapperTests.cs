using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Mappers;
using Maran.Modules.Monitoring.Resources;
using Maran.Modules.Monitoring.Tests.TestSupport;

namespace Maran.Modules.Monitoring.Tests.Mappers;

/// <summary>Behavioral contract of <see cref="PendingUpdateMapper"/>.</summary>
public sealed class PendingUpdateMapperTests
{
    /// <summary>Every field crosses as it arrived, including an absent installed version and source.</summary>
    /// <param name="installed">The installed version the agent reported, or <c>null</c>.</param>
    /// <param name="source">The source the agent reported, possibly empty.</param>
    [Theory]
    [InlineData("1.0", "updates")]
    [InlineData(null, "")]
    public void Every_field_crosses_as_it_arrived(string? installed, string source)
    {
        var dto = PendingUpdateMapper.ToDto(
            new AgentPendingUpdate("bash", "2.0", installed, source, AgentUpdateClassification.Ordinary),
            new StubStringLocalizer<DisplayNames>());

        Assert.Equal(
            new PendingUpdateDto("bash", "2.0", installed, source, "ordinary", "PendingUpdateClassificationOrdinary"),
            dto);
    }

    /// <summary>Each of the agent's three readings crosses as its own word, and unmeasured is never ordinary.</summary>
    /// <param name="classification">What the agent measured.</param>
    /// <param name="word">The word the row carries.</param>
    /// <param name="key">The display-name key that names the word.</param>
    [Theory]
    [InlineData(AgentUpdateClassification.Security, "security", "PendingUpdateClassificationSecurity")]
    [InlineData(AgentUpdateClassification.Ordinary, "ordinary", "PendingUpdateClassificationOrdinary")]
    [InlineData(AgentUpdateClassification.Unmeasured, "unmeasured", "PendingUpdateClassificationUnmeasured")]
    public void Each_classification_crosses_as_its_own_word_and_name(
        AgentUpdateClassification classification,
        string word,
        string key)
    {
        var dto = PendingUpdateMapper.ToDto(
            new AgentPendingUpdate("bash", "2.0", "1.0", "updates", classification),
            new StubStringLocalizer<DisplayNames>());

        Assert.Equal(word, dto.Classification);
        Assert.Equal(key, dto.ClassificationName);
    }

    /// <summary>A choice carries the word and the name the localizer gave it.</summary>
    [Fact]
    public void A_choice_carries_the_word_and_its_name()
    {
        var choice = PendingUpdateMapper.ToChoice("security", new StubStringLocalizer<DisplayNames>());

        Assert.Equal(new ClassificationChoiceDto("security", "PendingUpdateClassificationSecurity"), choice);
    }
}
