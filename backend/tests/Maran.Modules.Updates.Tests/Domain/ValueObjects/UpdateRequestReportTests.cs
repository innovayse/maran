using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Domain.ValueObjects;

namespace Maran.Modules.Updates.Tests.Domain.ValueObjects;

/// <summary>What the root update may say to the panel, and what the panel refuses to hold.</summary>
public sealed class UpdateRequestReportTests
{
    /// <summary>A word with plain release names is held as given.</summary>
    [Fact]
    public void A_word_with_plain_names_is_held()
    {
        Assert.True(UpdateRequestReport.TryCreate(PanelUpdateRunWord.Applied, "1.0.0", "1.1.0", out var report));
        Assert.NotNull(report);

        Assert.Equal(PanelUpdateRunWord.Applied, report.Word);
        Assert.Equal("1.0.0", report.From);
        Assert.Equal("1.1.0", report.To);
    }

    /// <summary>A name that is not plain is refused whichever of the two it is, and so is an undefined word.</summary>
    [Theory]
    [InlineData("1.0.0\nBcc: x@example.net", "1.1.0")]
    [InlineData("1.0.0", "../1.1.0")]
    [InlineData("1.0.0", "1.1.0;reboot")]
    [InlineData("", "1.1.0")]
    public void A_name_that_is_not_plain_is_refused(string from, string to)
    {
        Assert.False(UpdateRequestReport.TryCreate(PanelUpdateRunWord.RolledBack, from, to, out _));
    }

    /// <summary>An undefined word is refused.</summary>
    [Fact]
    public void An_undefined_word_is_refused()
    {
        Assert.False(UpdateRequestReport.TryCreate((PanelUpdateRunWord)99, "1.0.0", "1.1.0", out _));
    }

    /// <summary>The second name may be missing only for a word that cannot know it.</summary>
    [Theory]
    [InlineData(PanelUpdateRunWord.Unchanged, true)]
    [InlineData(PanelUpdateRunWord.Refused, true)]
    [InlineData(PanelUpdateRunWord.Installing, false)]
    [InlineData(PanelUpdateRunWord.Applied, false)]
    [InlineData(PanelUpdateRunWord.RolledBack, false)]
    [InlineData(PanelUpdateRunWord.RollbackFailed, false)]
    [InlineData(PanelUpdateRunWord.Interrupted, false)]
    public void The_second_name_may_be_missing_only_where_it_cannot_be_known(PanelUpdateRunWord word, bool accepted)
    {
        Assert.Equal(accepted, UpdateRequestReport.TryCreate(word, "1.0.0", null, out _));
    }
}
