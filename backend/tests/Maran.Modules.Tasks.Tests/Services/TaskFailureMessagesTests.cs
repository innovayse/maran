using System.Globalization;
using Maran.Modules.Tasks.Tests.TestSupport;

namespace Maran.Modules.Tasks.Tests.Services;

/// <summary>
/// A stored failure code becomes the sentence its own table writes, in the reader's culture, and a
/// code no table describes becomes the generic sentence — never the code itself.
/// </summary>
public sealed class TaskFailureMessagesTests
{
    /// <summary>A task that has not failed is described as nothing.</summary>
    [Fact]
    public void A_task_that_has_not_failed_is_described_as_nothing()
    {
        Assert.Null(TasksTestContext.FailureMessages().Describe(null));
    }

    /// <summary>A code a table describes reads as that table's sentence, in the reader's culture, with the code kept.</summary>
    /// <param name="culture">The reader's culture.</param>
    /// <param name="expected">The sentence the module's table writes for that culture.</param>
    [Theory]
    [InlineData("en", "The panel restarted while this task was running, so it did not finish.")]
    [InlineData("ru", "Панель перезапустилась, пока эта задача выполнялась, поэтому она не завершилась.")]
    [InlineData("hy", "Վահանակը վերագործարկվել է, մինչ այս առաջադրանքը կատարվում էր, ուստի այն չի ավարտվել։")]
    public void A_described_code_reads_as_its_own_sentence_in_the_readers_culture(string culture, string expected)
    {
        var failure = InCulture(culture, () =>
        {
            return TasksTestContext.FailureMessages().Describe("TaskAbandonedByRestart");
        });

        Assert.NotNull(failure);
        Assert.Equal("TaskAbandonedByRestart", failure.Code);
        Assert.Equal(expected, failure.Message);
    }

    /// <summary>A code no table describes reads as the generic sentence, and keeps the code beside it.</summary>
    [Fact]
    public void A_code_no_table_describes_reads_as_the_generic_sentence_with_the_code_kept()
    {
        var failure = InCulture("en", () =>
        {
            return TasksTestContext.FailureMessages().Describe("SomeRemovedModuleFailure");
        });

        Assert.NotNull(failure);
        Assert.Equal("SomeRemovedModuleFailure", failure.Code);
        Assert.StartsWith("The operation did not finish", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SomeRemovedModuleFailure", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Runs <paramref name="read"/> with the UI culture a request would have set, then restores it.</summary>
    /// <typeparam name="T">What the read returns.</typeparam>
    /// <param name="culture">The culture to read in.</param>
    /// <param name="read">The read.</param>
    /// <returns>What the read returned.</returns>
    private static T InCulture<T>(string culture, Func<T> read)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
