using Maran.Modules.Ssl.Options;
using Maran.SharedKernel.Utilities.Mail;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Ssl.Validators;

/// <summary>
/// Holds <see cref="AcmeOptions.ContactEmail"/> to the panel's single definition of a valid e-mail
/// address, at startup, so a contact the authority would reject fails the boot instead of the first
/// customer's order.
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

        return ValidateOptionsResult.Success;
    }
}
