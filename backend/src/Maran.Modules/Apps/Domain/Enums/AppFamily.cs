namespace Maran.Modules.Apps.Domain.Enums;

/// <summary>The four families of the app catalogue; an app id is a family and, for the runtimes, a version.</summary>
public enum AppFamily
{
    /// <summary>nginx as the site web server. Single version: the distribution's.</summary>
    Nginx = 1,

    /// <summary>Apache as the site web server. Single version: the distribution's.</summary>
    Apache = 2,

    /// <summary>A PHP version with its FPM runtime.</summary>
    Php = 3,

    /// <summary>A Node.js major version.</summary>
    Node = 4,
}
