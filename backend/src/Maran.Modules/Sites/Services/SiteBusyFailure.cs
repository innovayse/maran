using Maran.Sdk.Modules;

namespace Maran.Modules.Sites.Services;

/// <summary>Recognises the failure <see cref="SiteGuard"/> returns when a site stayed in use for the whole wait.</summary>
/// <remarks>
/// A handler journals its own refusals, so the one it did not produce itself - the lock's - is told apart
/// from the failures its body already journaled and is journaled once, here, by the caller.
/// </remarks>
public static class SiteBusyFailure
{
    /// <summary>Tells whether an error is the lock's "site busy" conflict.</summary>
    /// <param name="error">The error of a failed result, or null.</param>
    /// <returns>True when the error is <c>SiteBusy</c>.</returns>
    public static bool Is(Error? error)
    {
        return error is not null && error.Code == SiteOperationLock.BusyCode;
    }
}
