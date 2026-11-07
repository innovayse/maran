using System.Globalization;
using System.Resources;
using Maran.Modules.Identity.Commands.StartViewAs;

namespace Maran.Modules.Identity.Tests.Resources;

/// <summary>
/// The view-as refusal for an owner who has not accepted the invitation (issue #416) exists in english,
/// russian and armenian, is worded differently from the suspended-or-locked refusal, and does not say
/// the login is suspended.
/// </summary>
public sealed class ViewAsInvitationPendingTextTests
{
    /// <summary>The Identity module's error-message table, read straight from its resources.</summary>
    private static readonly ResourceManager ErrorTable =
        new("Maran.Modules.Identity.Resources.ErrorMessages", typeof(StartViewAsCommandHandler).Assembly);

    /// <summary>Each translated language, and the neutral english text.</summary>
    /// <param name="language">The language, empty for the neutral text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void The_pending_invitation_sentence_differs_from_the_suspended_one_in_every_language(string language)
    {
        var culture = language.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(language);

        var pending = ErrorTable.GetString("ViewAsInvitationPending", culture);
        var suspended = ErrorTable.GetString("ViewAsLoginUnavailable", culture);

        Assert.False(string.IsNullOrWhiteSpace(pending));
        Assert.NotEqual(suspended, pending);
        if (language.Length > 0)
        {
            Assert.NotEqual(ErrorTable.GetString("ViewAsInvitationPending", CultureInfo.InvariantCulture), pending);
        }
    }

    /// <summary>The english sentence names the invitation and does not call the login suspended or locked.</summary>
    [Fact]
    public void The_english_sentence_names_the_invitation_and_not_a_suspension()
    {
        var pending = ErrorTable.GetString("ViewAsInvitationPending", CultureInfo.InvariantCulture)!;

        Assert.Contains("invitation", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("suspended", pending, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("locked", pending, StringComparison.OrdinalIgnoreCase);
    }
}
