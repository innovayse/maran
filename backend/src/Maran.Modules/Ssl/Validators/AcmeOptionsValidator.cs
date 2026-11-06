using Maran.Modules.Ssl.Options;
using Maran.SharedKernel.Utilities.Mail;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Ssl.Validators;

/// <summary>
/// Holds <see cref="AcmeOptions.ContactEmail"/> to the panel's single definition of a valid e-mail
/// address and <see cref="AcmeOptions.DirectoryUrl"/> to an absolute https URL, at startup, so a
/// value the authority would reject fails the boot instead of the first customer's order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not the <c>[EmailAddress]</c> annotation this replaces.</b> That attribute asks only for
/// an <c>@</c> with something either side and imposes no ceiling at all, so it accepted
/// <c>Ops Team &lt;admin@example.com&gt;</c> and a kilobyte of text alike. The address is registered
/// with the ACME account and ends up in the authority's mail headers, which is precisely the value
/// <see cref="EmailAddressRule"/> exists to police (rules/security.md item 4).
/// </para>
/// <para>
/// <b>Why a type and not a lambda in <c>SslModule</c>.</b> The rule it enforces is now shared with
/// Identity and Monitoring, so its adoption here is worth a test of its own — and a validator that
/// can be constructed in one line is a validator a test can hold to account without building a
/// service provider (rules/csharp.md "Interfaces/, Options/ and Validators/").
/// </para>
/// <para>
/// <b>What the directory URL must be.</b> An absolute <c>https</c> URL with a host, no user
/// information (credentials in a URL end up in logs and in the account row's key), no fragment, and
/// no whitespace or control character anywhere in the text (rules/security.md item 4). A private
/// address is deliberately ALLOWED: an operator running a private certificate authority points the
/// panel at it, and that is a legitimate use, not an attack surface — the value is operator
/// configuration, never caller input. The authority's root certificate must be in the operating
/// system's trust store, or every call to it fails the TLS handshake.
/// </para>
/// </remarks>
public sealed class AcmeOptionsValidator : IValidateOptions<AcmeOptions>
{
    /// <summary>Validates the bound options.</summary>
    /// <param name="name">The named options instance being validated; unused, the section is unnamed.</param>
    /// <param name="options">The bound settings.</param>
    /// <returns>Success, or a failure naming the setting and what is wrong with it.</returns>
    public ValidateOptionsResult Validate(string? name, AcmeOptions options)
    {
        if (!EmailAddressRule.IsAddress(options.ContactEmail))
        {
            return ValidateOptionsResult.Fail(
                $"{AcmeOptions.SectionName}:{nameof(AcmeOptions.ContactEmail)} must be one bare e-mail address "
                + $"of at most {EmailAddressRule.MaximumLength} characters, with no display name and no control "
                + "characters.");
        }

        if (!IsAcceptableDirectoryUrl(options.DirectoryUrl))
        {
            return ValidateOptionsResult.Fail(
                $"{AcmeOptions.SectionName}:{nameof(AcmeOptions.DirectoryUrl)} must be an absolute https URL "
                + "with a host, no user information, no fragment, and no whitespace or control characters.");
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>Tells whether a directory URL is an absolute https URL carrying nothing a URL should not.</summary>
    /// <param name="value">The configured directory URL.</param>
    /// <returns><see langword="true"/> when the value is acceptable.</returns>
    private static bool IsAcceptableDirectoryUrl(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character) || character == '#')
            {
                return false;
            }
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0 && uri.UserInfo.Length == 0;
    }
}
