namespace Maran.Host.Dashboard;

/// <summary>The steps of the first-run checklist, in the order the card lists them.</summary>
/// <remarks>
/// A closed set the backend owns; the SPA writes each step's words in its own locale files and holds
/// no step of its own. Written on the wire as the camel-case name by the panel-wide string-enum
/// converter.
/// </remarks>
public enum FirstRunStepKey
{
    /// <summary>The administrator made a hosting plan of their own.</summary>
    Plan = 1,

    /// <summary>A hosting account exists.</summary>
    Account = 2,

    /// <summary>A site exists.</summary>
    Site = 3,

    /// <summary>Backups were pointed somewhere beyond the seeded local storage, or scheduled.</summary>
    BackupDestination = 4,

    /// <summary>A site certificate exists.</summary>
    Certificate = 5,
}
