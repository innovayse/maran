using FluentValidation;
using Maran.Modules.Marketplace.Resources;

namespace Maran.Modules.Marketplace.Queries.ListCatalogue;

/// <summary>
/// Validates <see cref="ListCatalogueQuery"/>: each text is plain and short before it is put in a request
/// to the marketplace, so nothing a caller types can reshape that request.
/// </summary>
public sealed class ListCatalogueQueryValidator : AbstractValidator<ListCatalogueQuery>
{
    /// <summary>The longest release channel name accepted.</summary>
    private const int ChannelMaxLength = 32;

    /// <summary>The longest search term accepted.</summary>
    private const int SearchMaxLength = 100;

    /// <summary>The longest cursor accepted.</summary>
    private const int CursorMaxLength = 256;

    /// <summary>Creates the validator.</summary>
    public ListCatalogueQueryValidator()
    {
        RuleFor(query => query.Channel)
            .Must(IsPlainChannel)
            .When(query => { return query.Channel is not null; })
            .WithMessage(nameof(ErrorMessages.MarketplaceChannelInvalid));

        RuleFor(query => query.Search)
            .Must(search => { return IsPlainText(search, SearchMaxLength); })
            .When(query => { return query.Search is not null; })
            .WithMessage(nameof(ErrorMessages.MarketplaceSearchInvalid));

        RuleFor(query => query.Cursor)
            .Must(cursor => { return IsPlainText(cursor, CursorMaxLength); })
            .When(query => { return query.Cursor is not null; })
            .WithMessage(nameof(ErrorMessages.MarketplaceCursorInvalid));
    }

    /// <summary>Tells whether a channel name is one to thirty-two lower-case letters, digits or hyphens starting with a letter.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>True when it is.</returns>
    private static bool IsPlainChannel(string? channel)
    {
        return channel is { Length: > 0 and <= ChannelMaxLength }
            && channel[0] is >= 'a' and <= 'z'
            && channel.All(character => { return character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'; });
    }

    /// <summary>Tells whether a text is short and holds no control character.</summary>
    /// <param name="text">The text.</param>
    /// <param name="maxLength">The longest accepted length.</param>
    /// <returns>True when it is.</returns>
    private static bool IsPlainText(string? text, int maxLength)
    {
        return text is not null && text.Length <= maxLength && !text.Any(char.IsControl);
    }
}
