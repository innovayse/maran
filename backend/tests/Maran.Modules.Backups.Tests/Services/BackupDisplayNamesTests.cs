using System.Globalization;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Tests.TestSupport;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>Covers the operator-facing names of a backup's verification state in every language.</summary>
public sealed class BackupDisplayNamesTests
{
    /// <summary>The languages the panel ships, each of which must name every verification state.</summary>
    private static readonly string[] Cultures = ["en", "ru", "hy"];

    /// <summary>Every verification state has a name in every language, and none falls back to its key.</summary>
    /// <remarks>
    /// Walks the enum rather than sampling it: a state added without a resx entry would print its own
    /// key (<c>BackupVerificationStateX</c>) in a table cell. The resolver is the real one over the
    /// real resource files, so a missing entry is observable here.
    /// </remarks>
    [Fact]
    public void Every_verification_state_has_a_name_in_every_language()
    {
        var names = BackupsTestContext.BackupNames();
        var previous = CultureInfo.CurrentUICulture;
        var unnamed = new List<string>();

        try
        {
            foreach (var culture in Cultures)
            {
                CultureInfo.CurrentUICulture = new CultureInfo(culture);

                foreach (var state in Enum.GetValues<BackupVerificationState>())
                {
                    var name = names.Of(state);

                    if (string.IsNullOrWhiteSpace(name)
                        || name.StartsWith("BackupVerificationState", StringComparison.Ordinal))
                    {
                        unnamed.Add(culture + ":" + state);
                    }
                }
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        Assert.Empty(unnamed);
    }

    /// <summary>The failed state reads as the Russian word, so a missing translation is caught.</summary>
    [Fact]
    public void The_failed_state_reads_in_the_callers_language()
    {
        var names = BackupsTestContext.BackupNames();
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");

            Assert.Equal("Проверка не пройдена", names.Of(BackupVerificationState.Failed));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
