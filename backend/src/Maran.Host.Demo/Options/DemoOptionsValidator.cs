using Maran.SharedKernel.Utilities.Mail;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Options;

/// <summary>
/// Checks what data annotations cannot: the abuse contact is present and well-formed, and the relations between
/// the budgets hold.
/// </summary>
public sealed class DemoOptionsValidator : IValidateOptions<DemoOptions>
{
    /// <summary>The longest an <c>https</c> abuse contact may be.</summary>
    private const int MaximumUrlLength = 300;

    /// <summary>Validates the options.</summary>
    /// <param name="name">The options name; unused.</param>
    /// <param name="options">The bound options.</param>
    /// <returns>Success, or a failure naming the relation that does not hold.</returns>
    public ValidateOptionsResult Validate(string? name, DemoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!IsContact(options.AbuseContact))
        {
            return ValidateOptionsResult.Fail(
                "Demo:AbuseContact is required and must be one bare e-mail address or one https URL: the demo "
                + "publishes what visitors type, and the banner tells them where to report abuse.");
        }

        if (options.VisitorMutationsPerWindow > options.GlobalMutationBudget)
        {
            return ValidateOptionsResult.Fail(
                "Demo:VisitorMutationsPerWindow must not exceed Demo:GlobalMutationBudget: one visitor "
                + "could otherwise be granted more than the whole demo has.");
        }

        if (options.VisitorSignInsPerWindow > options.GlobalSignInsPerWindow)
        {
            return ValidateOptionsResult.Fail(
                "Demo:VisitorSignInsPerWindow must not exceed Demo:GlobalSignInsPerWindow: one visitor "
                + "could otherwise be granted more sign-ins than the whole demo accepts.");
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>Whether a value is a contact a visitor can use: one bare address or one https URL.</summary>
    /// <param name="candidate">The configured value.</param>
    /// <returns>
    /// <c>true</c> for an address <see cref="EmailAddressRule"/> accepts, or an absolute <c>https</c> URL with a host
    /// and no user information, no whitespace and no control character.
    /// </returns>
    private static bool IsContact(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)
            || !string.Equals(candidate, candidate.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (EmailAddressRule.IsAddress(candidate))
        {
            return true;
        }

        return candidate.Length <= MaximumUrlLength
            && !candidate.Any(c => { return char.IsWhiteSpace(c) || char.IsControl(c); })
            && Uri.TryCreate(candidate, UriKind.Absolute, out var url)
            && url.Scheme == Uri.UriSchemeHttps
            && url.Host.Length > 0
            && url.UserInfo.Length == 0;
    }
}
