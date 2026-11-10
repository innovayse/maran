namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// The shape of a Node version as a site binds it — the major only, one or two digits, e.g. <c>22</c> —
/// stated once for every validator in the module that accepts one.
/// </summary>
/// <remarks>
/// Anchored with <c>\z</c>, not <c>$</c>, for the reason <see cref="PhpVersionFormat"/> gives: the value
/// names an installed runtime in a rendered unit. Whether that major is installed is not a question of
/// shape and is answered by the handler from the installed apps.
/// </remarks>
public static class NodeVersionFormat
{
    /// <summary>The regular expression a Node major must match in full.</summary>
    public const string Pattern = @"\A[0-9]{1,2}\z";
}
