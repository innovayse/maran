namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// The shape of a PHP version as the packages name it — one digit, a dot, one or two digits, e.g.
/// <c>8.3</c> — stated once for every validator in the module that accepts one.
/// </summary>
/// <remarks>
/// <para>
/// One pattern and not one per validator: a version accepted by one operation and refused by another
/// would let an operator install a runtime no site could then be pointed at, or filter a list by a
/// version no site could ever carry.
/// </para>
/// <para>
/// Anchored with <c>\z</c>, not <c>$</c>: in .NET <c>$</c> also matches before a trailing newline,
/// and the value names a php-fpm pool directory in a rendered config. The agent validates again on
/// its own side; this is the panel refusing early, not the boundary that matters (rules/security.md).
/// </para>
/// </remarks>
public static class PhpVersionFormat
{
    /// <summary>The regular expression a PHP version must match in full.</summary>
    public const string Pattern = @"\A[0-9]\.[0-9]{1,2}\z";
}
