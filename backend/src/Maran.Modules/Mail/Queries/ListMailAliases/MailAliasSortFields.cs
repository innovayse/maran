namespace Maran.Modules.Mail.Queries.ListMailAliases;

/// <summary>
/// The public names the aliases list sorts by — the <c>sort</c> parameter's vocabulary, written once and
/// read by <see cref="ListMailAliasesSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class MailAliasSortFields
{
    /// <summary>The whole address, <c>local@domain</c>, which is the column a screen shows first.</summary>
    public const string Address = "address";

    /// <summary>When the address was created.</summary>
    public const string CreatedAt = "createdAt";
}
