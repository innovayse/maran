namespace Maran.Modules.Files.Domain.Policies;

/// <summary>
/// The limits and the one reserved name that describe a path the file manager accepts.
/// </summary>
/// <remarks>
/// Values only: the rules that apply them are <c>FilesPathRule</c> and <c>FilesFileNameRule</c>. They
/// mirror the agent's own validation and nothing here is a control — the agent's descriptor walk is
/// (rules/security.md item 2). A reviewer must not accept "the validator stops traversal" as the answer
/// to a traversal finding.
/// </remarks>
public static class FilesPathPolicy
{
    /// <summary>The most bytes a whole relative path may be.</summary>
    public const int MaximumPathBytes = 4096;

    /// <summary>The most bytes one path segment may be.</summary>
    public const int MaximumSegmentBytes = 255;

    /// <summary>The most segments a relative path may have.</summary>
    public const int MaximumSegments = 64;

    /// <summary>The separator between segments of a relative path.</summary>
    public const char Separator = '/';

    /// <summary>
    /// The name of the account's trash directory, directly under the home. It is the agent's value
    /// (the trash lives at <c>&lt;home&gt;/.maran-trash</c>); the ordinary routes refuse it as a first
    /// segment, and the trash is reached only through the trash routes.
    /// </summary>
    public const string ReservedTrashName = ".maran-trash";
}
