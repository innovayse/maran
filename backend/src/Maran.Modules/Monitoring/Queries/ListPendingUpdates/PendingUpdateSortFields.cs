namespace Maran.Modules.Monitoring.Queries.ListPendingUpdates;

/// <summary>
/// The public names the pending updates list sorts by — the <c>sort</c> parameter's vocabulary, written
/// once and read by <see cref="ListPendingUpdatesSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
/// <remarks>
/// Deliberately no version fields: a version is not an ordered quantity. Package managers compare
/// versions by their own rules (epochs, tildes, release suffixes), the panel has neither
/// implementation, and an order that disagreed with the package manager's would be a wrong answer
/// that looks like a sorted one. Only the fields below have an order the panel can state exactly.
/// </remarks>
public static class PendingUpdateSortFields
{
    /// <summary>The package's name, which is what the list shows first.</summary>
    public const string PackageName = "packageName";

    /// <summary>The repository or origin the update comes from, which is empty for some rows.</summary>
    public const string Source = "source";

    /// <summary>
    /// The update's classification, ranked security, ordinary, then unmeasured, so ascending puts the
    /// security fixes first.
    /// </summary>
    public const string Security = "security";
}
