using Maran.Sdk.Resources;
using Maran.SharedKernel.Enums;

namespace Maran.Sdk.ModelBinding;

/// <summary>
/// The code that answers a search term the panel will not search for. The codes are defined by the
/// Sdk's own validation messages, which no other project can name, so a caller that must refuse a term
/// in the same words as the list binders asks here instead of spelling a code itself.
/// </summary>
public static class SearchTermRefusal
{
    /// <summary>The code for a term that was read but refused, or that was accepted yet too short to search.</summary>
    /// <param name="verdict">What reading the term concluded.</param>
    /// <returns>
    /// <c>ListSearchTooLong</c> for a term over the length limit, <c>ListSearchInvalid</c> for one carrying a
    /// control character, and <c>SearchTermTooShort</c> for every other case: a missing term, or one under the
    /// minimum length of the search that asked.
    /// </returns>
    public static string CodeFor(SearchTermVerdict verdict)
    {
        return verdict switch
        {
            SearchTermVerdict.TooLong => nameof(ValidationMessages.ListSearchTooLong),
            SearchTermVerdict.ControlCharacter => nameof(ValidationMessages.ListSearchInvalid),
            _ => nameof(ValidationMessages.SearchTermTooShort),
        };
    }
}
