using FluentValidation;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Validators;

/// <summary>
/// The panel's one rule for a password a person chooses for themselves after setup: not empty, no
/// longer than <see cref="MaxPasswordLength"/>, and at least the minimum length the operator's
/// security policy names. Shared through <c>SetValidator</c> by every command that sets a password
/// — reset, invitation and change — so the three cannot drift into accepting different passwords.
/// </summary>
/// <remarks>
/// <para>
/// The minimum is read from <see cref="SecurityPolicyCache"/>, asynchronously, on every validation:
/// an operator who raises it raises it on the next request, with nothing recompiled. The ceiling is a
/// constant because it is not policy but a bound on the work Argon2id is asked to do for one request.
/// </para>
/// <para>
/// Every failure carries the same code, <c>PasswordTooWeak</c>, whose text states the rule; the Host
/// forwards a validation message only when it is a bare resource key.
/// </para>
/// <para>
/// The first administrator's password (<c>CompleteSetupCommandValidator</c>) does not use this rule:
/// it is chosen before any policy row can exist and also refuses the username, a check this rule
/// cannot make because it sees the password alone.
/// </para>
/// </remarks>
public sealed class NewPasswordValidator : AbstractValidator<string>
{
    /// <summary>Longest password accepted, bounding the work Argon2id is asked to do.</summary>
    public const int MaxPasswordLength = 256;

    /// <summary>Configures the password rule.</summary>
    /// <param name="policyCache">The panel's security policy, read for the minimum password length.</param>
    public NewPasswordValidator(SecurityPolicyCache policyCache)
    {
        RuleFor(password => password)
            .NotEmpty().WithMessage(nameof(ErrorMessages.PasswordTooWeak))
            .MaximumLength(MaxPasswordLength).WithMessage(nameof(ErrorMessages.PasswordTooWeak))
            .MustAsync(async (password, cancellationToken) =>
            {
                var policy = await policyCache.GetAsync(cancellationToken);
                return password is not null && password.Length >= policy.MinimumPasswordLength;
            })
            .WithMessage(nameof(ErrorMessages.PasswordTooWeak));
    }
}
