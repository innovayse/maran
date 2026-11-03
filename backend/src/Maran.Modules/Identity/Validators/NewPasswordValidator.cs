using FluentValidation;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Validators;

/// <summary>
/// The panel's one rule for the LENGTH of a password a person chooses for themselves after setup: not
/// empty, no longer than <see cref="MaxPasswordLength"/>, and at least the minimum length the
/// operator's security policy names. Shared through <c>SetValidator</c> by every command that sets a
/// password — reset, invitation and change — so the three cannot drift into accepting different
/// passwords.
/// </summary>
/// <remarks>
/// <para>
/// The minimum is read from <see cref="SecurityPolicyCache"/>, asynchronously, on every validation:
/// an operator who raises it raises it on the next request, with nothing recompiled. The ceiling is a
/// constant because it is not policy but a bound on the work Argon2id is asked to do for one request.
/// </para>
/// <para>
/// Every failure carries the same code, <c>PasswordTooWeak</c>, and the operator's minimum as the
/// <c>MinLength</c> placeholder, which the Host hands to the text as <c>{1}</c> — the sentence names
/// the length the policy requires, not a length written into the resource. The Host forwards a
/// validation message only when it is a bare resource key.
/// </para>
/// <para>
/// This rule sees the password alone, so it cannot refuse a password equal to the username. That
/// half of the policy (<see cref="PasswordPolicy.DiffersFromUsername"/>) is applied where the
/// username is known: <c>CompleteSetupCommandValidator</c> for the first administrator and
/// <c>ChangePasswordCommandHandler</c> for a signed-in change. It is deliberately NOT applied to a
/// reset or an invitation: those commands carry an unauthenticated token, and a refusal that depends
/// on the account the token names would tell the caller the token is real before it is spent.
/// </para>
/// </remarks>
public sealed class NewPasswordValidator : AbstractValidator<string>
{
    /// <summary>Longest password accepted, bounding the work Argon2id is asked to do.</summary>
    public const int MaxPasswordLength = PasswordPolicy.MaxLength;

    /// <summary>The placeholder name carrying the minimum length, read by the Host as <c>{1}</c>.</summary>
    public const string MinimumLengthPlaceholder = "MinLength";

    /// <summary>Configures the password rule.</summary>
    /// <param name="policyCache">The panel's security policy, read for the minimum password length.</param>
    public NewPasswordValidator(SecurityPolicyCache policyCache)
    {
        RuleFor(password => password)
            .MustAsync(async (_, password, context, cancellationToken) =>
            {
                var policy = await policyCache.GetAsync(cancellationToken);
                context.MessageFormatter.AppendArgument(MinimumLengthPlaceholder, policy.MinimumPasswordLength);
                return PasswordPolicy.HasAcceptableLength(password, policy.MinimumPasswordLength);
            })
            .WithMessage(nameof(ErrorMessages.PasswordTooWeak));
    }
}
