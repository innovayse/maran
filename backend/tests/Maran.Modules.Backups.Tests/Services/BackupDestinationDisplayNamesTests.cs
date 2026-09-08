using System.Globalization;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Seeders;
using Maran.Modules.Backups.Tests.TestSupport;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>
/// Covers which destination names the panel translates and which it leaves exactly as an operator
/// typed them.
/// </summary>
/// <remarks>
/// The pinned defect, measured in a browser: the backup destinations screen headed its only card
/// with "Local storage" while every other word on the page was Russian. The stored name is right to
/// stay English — a name translated at write time would freeze in whichever language the server
/// first booted in — so what was missing was the panel stating what to SHOW for it.
/// </remarks>
public sealed class BackupDestinationDisplayNamesTests
{
    /// <summary>The seeded destination is shown in the caller's language, not the row's English.</summary>
    /// <remarks>
    /// The VALUE is asserted rather than "a name came back": the defect was a name coming back, and
    /// it was the wrong one.
    /// </remarks>
    [Fact]
    public void The_panels_own_destination_is_shown_in_the_callers_language()
    {
        var names = BackupsTestContext.DestinationNames();
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");

            var displayName = names.Of(
                BackupDestination.DefaultDestinationId,
                DefaultBackupDestinationSeeder.DefaultDestinationName);

            Assert.Equal("Локальное хранилище", displayName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>In English the panel's own destination reads as the row already reads.</summary>
    [Fact]
    public void The_panels_own_destination_reads_the_same_in_english()
    {
        var names = BackupsTestContext.DestinationNames();

        var displayName = names.Of(
            BackupDestination.DefaultDestinationId,
            DefaultBackupDestinationSeeder.DefaultDestinationName);

        Assert.Equal("Local storage", displayName);
    }

    /// <summary>A destination an operator named keeps the operator's own words, in every language.</summary>
    /// <remarks>
    /// The branch that stops this from becoming a translator of other people's text. A rewrite that
    /// keyed on the stored NAME rather than on the row's identity would pass the tests above and
    /// would rename a customer's own "Local storage" into Russian behind their back.
    /// </remarks>
    [Fact]
    public void A_destination_an_operator_named_keeps_the_operators_own_words()
    {
        var names = BackupsTestContext.DestinationNames();
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");

            Assert.Equal("Local storage", names.Of(Guid.NewGuid(), "Local storage"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
