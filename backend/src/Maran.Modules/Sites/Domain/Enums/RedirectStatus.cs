namespace Maran.Modules.Sites.Domain.Enums;

/// <summary>The HTTP status a redirect rule answers with.</summary>
/// <remarks>
/// A closed set rather than a free number: anything else a caller could type is either not a redirect
/// or has a meaning (307, 308) this panel does not promise to render. The numeric values are the HTTP
/// status codes themselves, so the mapping to the wire is a cast and not a second table.
/// </remarks>
public enum RedirectStatus
{
    /// <summary>301: the move is permanent, and clients and search engines may remember it.</summary>
    Permanent = 301,

    /// <summary>302: the move is temporary, and clients keep asking this address.</summary>
    Temporary = 302,
}
