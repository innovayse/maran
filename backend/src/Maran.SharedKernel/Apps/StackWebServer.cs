namespace Maran.SharedKernel.Apps;

/// <summary>Which web server serves this server's sites, as the app store reports it to another module.</summary>
/// <remarks>
/// A deliberate second declaration of a closed set the Apps module owns, for the reason the Sites module
/// declares its own web-server enum: a module may not reference another, so a cross-module answer cannot
/// carry the owning module's enum. Exactly one web server is installed at a time, and
/// <see cref="None"/> says none is (the first-run state), which is not a web server.
/// </remarks>
public enum StackWebServer
{
    /// <summary>No site web server is installed: the stack is not ready.</summary>
    None = 0,

    /// <summary>nginx owns ports 80 and 443 for hosted sites.</summary>
    Nginx = 1,

    /// <summary>Apache owns ports 80 and 443 for hosted sites.</summary>
    Apache = 2,
}
