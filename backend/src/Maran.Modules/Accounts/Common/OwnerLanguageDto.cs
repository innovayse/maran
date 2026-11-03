namespace Maran.Modules.Accounts.Common;

/// <summary>One language an account owner can be written to in, with the name an administrator picks it by.</summary>
/// <param name="Language">
/// The language's two-letter name on the wire (<c>en</c>, <c>ru</c>, <c>hy</c>): exactly what
/// <c>CreateAccountCommand.OwnerLanguage</c> accepts, so the form sends it back unchanged.
/// </param>
/// <param name="LanguageDisplayName">The language's name resolved server-side, written in that language itself.</param>
public sealed record OwnerLanguageDto(string Language, string LanguageDisplayName);
