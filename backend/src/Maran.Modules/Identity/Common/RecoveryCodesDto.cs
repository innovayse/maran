namespace Maran.Modules.Identity.Common;

/// <summary>
/// The recovery codes, returned the one and only time they are readable. Only hashes are stored, so
/// a user who does not save them now cannot be shown them again — the screen says so, and this type
/// exists solely to carry them across that one response.
/// </summary>
/// <param name="Codes">The plaintext codes, in the order they should be shown.</param>
public sealed record RecoveryCodesDto(IReadOnlyList<string> Codes);
