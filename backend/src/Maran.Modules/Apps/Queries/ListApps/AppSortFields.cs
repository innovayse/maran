namespace Maran.Modules.Apps.Queries.ListApps;

/// <summary>
/// The public names the app list sorts by - the <c>sort</c> parameter's vocabulary, written once and read by
/// <see cref="ListAppsSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class AppSortFields
{
    /// <summary>The catalogue id.</summary>
    public const string AppId = "appId";

    /// <summary>The app's name in the request's language.</summary>
    public const string Name = "name";

    /// <summary>What the app is for.</summary>
    public const string Kind = "kind";

    /// <summary>Where the app stands in the ledger.</summary>
    public const string State = "state";

    /// <summary>When the panel first saw the app installed.</summary>
    public const string InstalledAt = "installedAt";
}
