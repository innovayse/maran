namespace Maran.Host.Dashboard;

/// <summary>The screen a first-run step sends the administrator to.</summary>
/// <remarks>
/// A closed set the backend owns: the SPA maps each value to one of its routes and invents neither a
/// route nor a value, so a step can never point at a screen the backend did not name. Written on the
/// wire as the camel-case name.
/// </remarks>
public enum FirstRunRoute
{
    /// <summary>The hosting plans list.</summary>
    Plans = 1,

    /// <summary>The hosting accounts list.</summary>
    Accounts = 2,

    /// <summary>The sites list.</summary>
    Sites = 3,

    /// <summary>The backup destinations screen.</summary>
    BackupDestinations = 4,

    /// <summary>The sites list, where each site's certificate is installed.</summary>
    Certificates = 5,
}
