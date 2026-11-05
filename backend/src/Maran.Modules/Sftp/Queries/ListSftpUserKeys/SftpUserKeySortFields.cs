namespace Maran.Modules.Sftp.Queries.ListSftpUserKeys;

/// <summary>
/// The public names the keys list sorts by — the <c>sort</c> parameter's vocabulary, written once and
/// read by <see cref="ListSftpUserKeysSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class SftpUserKeySortFields
{
    /// <summary>The customer's name for the key.</summary>
    public const string Label = "label";

    /// <summary>When the key was registered.</summary>
    public const string CreatedAt = "createdAt";
}
