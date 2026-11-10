namespace Maran.Modules.Marketplace.Domain.Policies;

/// <summary>
/// What the marketplace module accepts as a module id before it is put in a URL, a lock, a task or an
/// audit entry: one to sixty-four lower-case ASCII letters or digits, starting with a letter. The same
/// shape the audit journal writes without change, so an id that passes here is never replaced by a
/// placeholder there.
/// </summary>
public static class ModuleIdPolicy
{
    /// <summary>The longest module id accepted.</summary>
    public const int MaxLength = 64;

    /// <summary>Tells whether a text is an acceptable module id.</summary>
    /// <param name="id">The text, or null.</param>
    /// <returns>True when it is one to sixty-four lower-case letters or digits starting with a letter.</returns>
    public static bool IsValid(string? id)
    {
        return id is { Length: > 0 and <= MaxLength }
            && id[0] is >= 'a' and <= 'z'
            && id.All(character => { return character is >= 'a' and <= 'z' or >= '0' and <= '9'; });
    }
}
